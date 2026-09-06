using System.Globalization;
using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Catalog.Products;

/// <summary>
/// Product photography.
///
/// The one structural idea here: an image either belongs to the product or to
/// one of its variants. Product-level images are the general shots; a variant's
/// images are that shade's own. The storefront shows the selected variant's
/// gallery and falls back to the product's when the variant has none, so a
/// product with eight shades needs eight photographs, not eight full galleries.
/// </summary>
public partial class ProductAdminService
{
    /// <summary>
    /// Past this the gallery stops being a gallery. Generous enough for a
    /// product with several shades, low enough that nobody uploads a folder.
    /// </summary>
    public const int MaxImagesPerProduct = 24;

    public async Task<IReadOnlyList<ProductImageDetail>> GetImagesAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        var images = await _db.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.ProductVariantId)
            .ThenBy(i => i.DisplayOrder)
            .Select(i => new ProductImageDetail
            {
                Id = i.Id,
                ProductVariantId = i.ProductVariantId,
                VariantName = i.ProductVariant == null ? null : i.ProductVariant.VariantName,
                Path = i.Path,
                AltText = i.AltText,
                DisplayOrder = i.DisplayOrder,
                IsPrimary = i.IsPrimary,
            })
            .ToListAsync(cancellationToken);

        return images;
    }

    /// <summary>
    /// Stores one uploaded image and attaches it to the product, or to one of
    /// its variants.
    ///
    /// The file is written first and the row second. The other order would risk
    /// a row pointing at a file that never arrived, which renders as a broken
    /// image on the storefront; this order can at worst leave an unreferenced
    /// file on disk, which nobody sees.
    /// </summary>
    public async Task<OperationResult<long>> AddImageAsync(
        long productId,
        UploadImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return OperationResult<long>.Failure("That product no longer exists.");
        }

        if (request.ProductVariantId is not null
            && product.Variants.All(v => v.Id != request.ProductVariantId))
        {
            return OperationResult<long>.Failure("That variant does not belong to this product.");
        }

        var existingCount = await _db.ProductImages.CountAsync(i => i.ProductId == productId, cancellationToken);

        if (existingCount >= MaxImagesPerProduct)
        {
            return OperationResult<long>.Failure(
                $"This product already has {MaxImagesPerProduct} images, which is the limit. "
                + "Remove one before adding another.");
        }

        string storedPath;

        try
        {
            storedPath = await _files.SaveAsync(request.Content, "products", request.Kind, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // The store refuses empty files, oversized files and anything whose
            // bytes do not match the type it claims to be. Those are all things
            // the person can fix, so they come back as messages rather than as
            // a 500.
            return OperationResult<long>.Failure(ex.Message);
        }

        // First image in its gallery becomes the primary one. Someone
        // uploading a single photograph should not have to also nominate it.
        var galleryHasPrimary = await _db.ProductImages.AnyAsync(
            i => i.ProductId == productId
                 && i.ProductVariantId == request.ProductVariantId
                 && i.IsPrimary,
            cancellationToken);

        var nextOrder = await _db.ProductImages
            .Where(i => i.ProductId == productId && i.ProductVariantId == request.ProductVariantId)
            .MaxAsync(i => (int?)i.DisplayOrder, cancellationToken) ?? -1;

        var image = new ProductImage
        {
            ProductId = productId,
            ProductVariantId = request.ProductVariantId,
            Path = storedPath,

            // Alt text is required by the schema because an image with none is
            // invisible to search and unusable on a screen reader. Defaulting
            // it beats blocking the upload; the editor shows it for correction.
            AltText = string.IsNullOrWhiteSpace(request.AltText)
                ? BuildDefaultAltText(product, request.ProductVariantId)
                : request.AltText.Trim(),
            DisplayOrder = nextOrder + 1,
            IsPrimary = !galleryHasPrimary,
        };

        _db.ProductImages.Add(image);
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(image.Id);
    }

    /// <summary>
    /// Saves the alt text, ordering and primary flag for a product's images in
    /// one go, matching how the editor posts them.
    /// </summary>
    public async Task<OperationResult> SaveImagesAsync(
        long productId,
        SaveImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var images = await _db.ProductImages
            .Where(i => i.ProductId == productId)
            .ToListAsync(cancellationToken);

        if (images.Count == 0)
        {
            return OperationResult.Success();
        }

        var byId = images.ToDictionary(i => i.Id);
        var inputs = request.Images.Where(i => byId.ContainsKey(i.Id)).ToList();

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.AltText))
            {
                return OperationResult.Failure(
                    "Every image needs alt text - it is what a shopper using a screen reader, "
                    + "and every search engine, actually reads.");
            }

            if (input.AltText.Trim().Length > 250)
            {
                return OperationResult.Failure("That alt text is too long.");
            }
        }

        // Exactly one primary per gallery, where the product-level images count
        // as their own gallery. The database enforces this with a filtered
        // unique index; checking here is what turns a constraint violation into
        // a sentence.
        var galleries = inputs
            .Select(i => (Input: i, Gallery: byId[i.Id].ProductVariantId))
            .GroupBy(x => x.Gallery);

        foreach (var gallery in galleries)
        {
            if (gallery.Count(x => x.Input.IsPrimary) > 1)
            {
                return OperationResult.Failure(
                    "Only one image in each gallery can be the main one.");
            }
        }

        // Cleared first and set second, in separate round trips: EF gives no
        // ordering guarantee between the row losing the flag and the row taking
        // it, and the index would reject the overlap.
        foreach (var input in inputs)
        {
            var image = byId[input.Id];
            image.AltText = input.AltText.Trim();
            image.DisplayOrder = input.DisplayOrder;
            image.IsPrimary = false;
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var input in inputs.Where(i => i.IsPrimary))
        {
            byId[input.Id].IsPrimary = true;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.ProductUpdated,
            nameof(Product),
            productId.ToString(CultureInfo.InvariantCulture),
            "Updated product images.",
            new { ImageCount = inputs.Count },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Removes an image, and hands the primary flag to the next one in its
    /// gallery so no gallery is left without a thumbnail.
    /// </summary>
    public async Task<OperationResult> DeleteImageAsync(
        long productId,
        long imageId,
        CancellationToken cancellationToken = default)
    {
        // Scoped to the product from the route, so an id from the browser can
        // only ever reach an image on the product the caller is already editing.
        var image = await _db.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId, cancellationToken);

        if (image is null)
        {
            return OperationResult.Failure("That image no longer exists.");
        }

        var wasPrimary = image.IsPrimary;
        var gallery = image.ProductVariantId;
        var path = image.Path;

        _db.ProductImages.Remove(image);
        await _db.SaveChangesAsync(cancellationToken);

        if (wasPrimary)
        {
            var replacement = await _db.ProductImages
                .Where(i => i.ProductId == productId && i.ProductVariantId == gallery)
                .OrderBy(i => i.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken);

            if (replacement is not null)
            {
                replacement.IsPrimary = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // The row is gone either way. A file that outlives its row is litter;
        // a row that outlives its file is a broken image on the storefront.
        await _files.DeleteAsync(path, cancellationToken);

        return OperationResult.Success();
    }

    private static string BuildDefaultAltText(Product product, long? variantId)
    {
        var variant = variantId is null
            ? null
            : product.Variants.FirstOrDefault(v => v.Id == variantId);

        return variant is null || variant.VariantName == DefaultVariantName
            ? product.Name
            : $"{product.Name} - {variant.VariantName}";
    }
}

public class ProductImageDetail
{
    public long Id { get; set; }

    public long? ProductVariantId { get; set; }

    /// <summary>Null for the product-level gallery.</summary>
    public string? VariantName { get; set; }

    public string Path { get; set; } = string.Empty;

    public string AltText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }
}

public class UploadImageRequest
{
    public required Stream Content { get; init; }

    public required FileKind Kind { get; init; }

    public long? ProductVariantId { get; init; }

    public string? AltText { get; init; }
}

public class ImageInput
{
    public long Id { get; set; }

    public string AltText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }
}

public class SaveImagesRequest
{
    public IReadOnlyList<ImageInput> Images { get; set; } = [];
}
