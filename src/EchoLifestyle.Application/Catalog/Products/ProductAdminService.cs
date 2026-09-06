using System.Globalization;
using System.Text.RegularExpressions;
using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Catalog.Products;

/// <summary>
/// Product administration.
///
/// Split across two files: this one owns the product record itself, and
/// ProductAdminService.Variants.cs owns options, the variant matrix and
/// pricing. They are one class because they are one transaction boundary -
/// changing a product's options rewrites its variants, and neither half is
/// meaningful alone.
/// </summary>
public partial class ProductAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _files;

    public ProductAdminService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        IAuditLogger audit,
        IFileStorage files)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _files = files;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-\.]{1,39}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-\._]{1,39}$")]
    private static partial Regex SkuPattern();

    public async Task<PagedResult<ProductListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        long? brandId,
        long? categoryId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Products.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(p => p.IsActive),
            StatusFilter.Inactive => query.Where(p => !p.IsActive),
            _ => query,
        };

        if (brandId is not null)
        {
            query = query.Where(p => p.BrandId == brandId);
        }

        if (categoryId is not null)
        {
            // Descendants included: filtering by Skincare has to return the
            // night creams too, which is what the materialised path is for.
            var path = await _db.Categories
                .Where(c => c.Id == categoryId)
                .Select(c => c.Path)
                .FirstOrDefaultAsync(cancellationToken);

            if (path is not null)
            {
                query = query.Where(p => p.ProductCategories.Any(pc => pc.Category!.Path.StartsWith(path)));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{term}%")
                || EF.Functions.Like(p.Code, $"%{term}%")
                || EF.Functions.Like(p.Brand!.Name, $"%{term}%")
                || p.Variants.Any(v => EF.Functions.Like(v.Sku, $"%{term}%")
                                       || (v.Barcode != null && EF.Functions.Like(v.Barcode, $"%{term}%"))));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("name", true) => query.OrderByDescending(p => p.Name),
            ("code", false) => query.OrderBy(p => p.Code),
            ("code", true) => query.OrderByDescending(p => p.Code),
            ("brand", false) => query.OrderBy(p => p.Brand!.Name).ThenBy(p => p.Name),
            ("brand", true) => query.OrderByDescending(p => p.Brand!.Name).ThenBy(p => p.Name),
            ("isActive", false) => query.OrderBy(p => p.IsActive).ThenBy(p => p.Name),
            ("isActive", true) => query.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name),
            ("isPublished", false) => query.OrderBy(p => p.IsPublished).ThenBy(p => p.Name),
            ("isPublished", true) => query.OrderByDescending(p => p.IsPublished).ThenBy(p => p.Name),
            _ => query.OrderBy(p => p.Name),
        };

        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(p => new ProductListItem
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                BrandName = p.Brand!.Name,
                PrimaryCategoryName = p.ProductCategories
                    .Where(pc => pc.IsPrimary)
                    .Select(pc => pc.Category!.Name)
                    .FirstOrDefault(),
                VariantCount = p.Variants.Count(v => v.IsActive),
                PriceFrom = p.Variants
                    .Where(v => v.IsActive)
                    .SelectMany(v => v.PriceListItems)
                    .Where(i => i.PriceListId == priceListId && i.EffectiveToUtc == null)
                    .Min(i => (decimal?)i.UnitPrice),
                PriceTo = p.Variants
                    .Where(v => v.IsActive)
                    .SelectMany(v => v.PriceListItems)
                    .Where(i => i.PriceListId == priceListId && i.EffectiveToUtc == null)
                    .Max(i => (decimal?)i.UnitPrice),
                IsActive = p.IsActive,
                IsPublished = p.IsPublished,
                IsBatchTracked = p.IsBatchTracked,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductListItem>(rows, totalCount, filteredCount);
    }

    public async Task<ProductDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.ProductCategories)
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        var prices = await _db.PriceListItems
            .AsNoTracking()
            .Where(i => i.PriceListId == priceListId
                        && i.EffectiveToUtc == null
                        && i.ProductVariant!.ProductId == id)
            .Select(i => new { i.ProductVariantId, i.UnitPrice })
            .ToListAsync(cancellationToken);

        var priceByVariant = prices.ToDictionary(p => p.ProductVariantId, p => p.UnitPrice);

        // A variant whose option combination no longer exists has had its
        // option links removed, so an active product with options and a variant
        // holding none of them is a retired one.
        var hasOptions = product.Options.Count > 0;

        return new ProductDetail
        {
            Id = product.Id,
            Code = product.Code,
            Name = product.Name,
            Slug = product.Slug,
            BrandId = product.BrandId,
            BrandName = product.Brand?.Name ?? string.Empty,
            UnitOfMeasureId = product.UnitOfMeasureId,
            ShortDescription = product.ShortDescription,
            LongDescription = product.LongDescription,
            HowToUse = product.HowToUse,
            Ingredients = product.Ingredients,
            IsBatchTracked = product.IsBatchTracked,
            IsExpiryTracked = product.IsExpiryTracked,
            ShelfLifeDays = product.ShelfLifeDays,
            IsActive = product.IsActive,
            IsPublished = product.IsPublished,
            PublishedAtUtc = product.PublishedAtUtc,
            CategoryIds = product.ProductCategories.Select(pc => pc.CategoryId).ToList(),
            PrimaryCategoryId = product.ProductCategories
                .Where(pc => pc.IsPrimary)
                .Select(pc => (long?)pc.CategoryId)
                .FirstOrDefault(),
            Options = product.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new ProductOptionDetail
                {
                    Id = o.Id,
                    Name = o.Name,
                    DisplayOrder = o.DisplayOrder,
                    Values = o.Values
                        .OrderBy(v => v.DisplayOrder)
                        .Select(v => new ProductOptionValueDetail
                        {
                            Id = v.Id,
                            Value = v.Value,
                            SwatchHex = v.SwatchHex,
                            DisplayOrder = v.DisplayOrder,
                        })
                        .ToList(),
                })
                .ToList(),
            Variants = product.Variants
                .OrderBy(v => v.DisplayOrder)
                .ThenBy(v => v.Sku, StringComparer.OrdinalIgnoreCase)
                .Select(v => new ProductVariantDetail
                {
                    Id = v.Id,
                    Sku = v.Sku,
                    Barcode = v.Barcode,
                    VariantName = v.VariantName,
                    Mrp = v.Mrp,
                    CompareAtPrice = v.CompareAtPrice,
                    WeightGrams = v.WeightGrams,
                    CurrentPrice = priceByVariant.TryGetValue(v.Id, out var price) ? price : null,
                    IsDefault = v.IsDefault,
                    IsActive = v.IsActive,
                    DisplayOrder = v.DisplayOrder,
                    IsRetired = hasOptions && v.OptionValues.Count == 0,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// The one-screen create path.
    ///
    /// Everything a product strictly needs is derived rather than demanded: the
    /// code, the slug, the unit and the single default variant's SKU all have
    /// defensible defaults. What comes out is a complete, correctly shaped
    /// product - one default variant, one open price - not a draft that has to
    /// be finished elsewhere before it works.
    /// </summary>
    public async Task<OperationResult<long>> QuickCreateAsync(
        QuickCreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<long>.Failure(
                "Enter a product name.", nameof(QuickCreateProductRequest.Name));
        }

        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == request.BrandId, cancellationToken);

        if (brand is null)
        {
            return OperationResult<long>.Failure(
                "Choose a brand.", nameof(QuickCreateProductRequest.BrandId));
        }

        var unitId = request.UnitOfMeasureId ?? await DefaultUnitIdAsync(cancellationToken);

        if (unitId is null)
        {
            return OperationResult<long>.Failure(
                "No unit of measure exists. Seed the catalogue baseline before adding products.");
        }

        if (request.Price is < 0)
        {
            return OperationResult<long>.Failure(
                "Price cannot be negative.", nameof(QuickCreateProductRequest.Price));
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await NextProductCodeAsync(cancellationToken)
            : request.Code.Trim().ToUpperInvariant();

        if (!CodePattern().IsMatch(code))
        {
            return OperationResult<long>.Failure(
                "Use 2 to 40 characters: letters, numbers, dashes and dots.",
                nameof(QuickCreateProductRequest.Code));
        }

        if (await _db.Products.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return OperationResult<long>.Failure(
                $"Product code '{code}' is already in use.", nameof(QuickCreateProductRequest.Code));
        }

        var slug = await ResolveProductSlugAsync(name, null, existingId: null, cancellationToken);
        if (!slug.Succeeded)
        {
            return OperationResult<long>.Failure(slug.Error!, nameof(QuickCreateProductRequest.Name));
        }

        var sku = string.IsNullOrWhiteSpace(request.Sku) ? code : request.Sku.Trim().ToUpperInvariant();

        var skuCheck = await ValidateSkuAsync(sku, excludingVariantId: null, cancellationToken);
        if (!skuCheck.Succeeded)
        {
            return OperationResult<long>.Failure(skuCheck.Error!, nameof(QuickCreateProductRequest.Sku));
        }

        var barcode = Trim(request.Barcode);
        if (barcode is not null)
        {
            var barcodeCheck = await ValidateBarcodeAsync(barcode, excludingVariantId: null, cancellationToken);
            if (!barcodeCheck.Succeeded)
            {
                return OperationResult<long>.Failure(
                    barcodeCheck.Error!, nameof(QuickCreateProductRequest.Barcode));
            }
        }

        var product = new Product
        {
            Code = code,
            Name = name,
            Slug = slug.Value!,
            BrandId = brand.Id,
            UnitOfMeasureId = unitId.Value,
            IsBatchTracked = request.IsBatchTracked,

            // Expiry tracking implies a batch to hang the date on. Accepting
            // one without the other would produce expiry dates nobody could
            // trace to a delivery.
            IsExpiryTracked = request.IsExpiryTracked,
            IsActive = request.IsActive,
            IsPublished = false,
        };

        if (product.IsExpiryTracked)
        {
            product.IsBatchTracked = true;
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.CategoryId is not null)
        {
            _db.ProductCategories.Add(new ProductCategory
            {
                ProductId = product.Id,
                CategoryId = request.CategoryId.Value,
                IsPrimary = true,
            });
        }

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = sku,
            Barcode = barcode,
            VariantName = DefaultVariantName,
            Mrp = request.Mrp,
            IsDefault = true,
            IsActive = true,
            DisplayOrder = 0,
        };

        _db.ProductVariants.Add(variant);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.Price is not null)
        {
            await SetPriceAsync(variant, request.Price.Value, cancellationToken);
        }

        await _audit.LogAsync(
            AuditActions.ProductCreated,
            nameof(Product),
            product.Id.ToString(CultureInfo.InvariantCulture),
            $"Created product {product.Code} - {product.Name}.",
            new { product.Code, product.Name, Brand = brand.Name, Sku = sku, request.Price },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(product.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(p => p.ProductCategories)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return OperationResult.Failure("That product no longer exists.");
        }

        var validation = await ValidateProductAsync(request, id, cancellationToken);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var slug = await ResolveProductSlugAsync(request.Name, request.Slug, id, cancellationToken);
        if (!slug.Succeeded)
        {
            return OperationResult.Failure(slug.Error!, nameof(SaveProductRequest.Slug));
        }

        var categoryResult = await ApplyCategoriesAsync(product, request, cancellationToken);
        if (!categoryResult.Succeeded)
        {
            return categoryResult;
        }

        var before = new
        {
            product.Code,
            product.Name,
            product.BrandId,
            product.IsActive,
            product.IsBatchTracked,
            product.IsExpiryTracked,
        };

        product.Code = request.Code.Trim().ToUpperInvariant();
        product.Name = request.Name.Trim();
        product.Slug = slug.Value!;
        product.BrandId = request.BrandId;
        product.UnitOfMeasureId = request.UnitOfMeasureId;
        product.ShortDescription = Trim(request.ShortDescription);
        product.LongDescription = Trim(request.LongDescription);
        product.HowToUse = Trim(request.HowToUse);
        product.Ingredients = Trim(request.Ingredients);
        product.IsBatchTracked = request.IsBatchTracked || request.IsExpiryTracked;
        product.IsExpiryTracked = request.IsExpiryTracked;
        product.ShelfLifeDays = request.ShelfLifeDays;
        product.IsActive = request.IsActive;

        // Deactivating a product also takes it off the storefront. Leaving it
        // published would advertise something the ERP refuses to sell.
        if (!product.IsActive && product.IsPublished)
        {
            product.IsPublished = false;
            product.PublishedAtUtc = null;
        }

        await _audit.LogAsync(
            AuditActions.ProductUpdated,
            nameof(Product),
            product.Id.ToString(CultureInfo.InvariantCulture),
            $"Updated product {product.Code} - {product.Name}.",
            new { Before = before, After = new { product.Code, product.Name, product.BrandId, product.IsActive, product.IsBatchTracked, product.IsExpiryTracked } },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Publishing is a separate action with its own permission: preparing a
    /// product and putting it in front of customers are different decisions,
    /// often made by different people.
    /// </summary>
    public async Task<OperationResult> SetPublishedAsync(
        long id,
        bool published,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return OperationResult.Failure("That product no longer exists.");
        }

        if (published)
        {
            if (!product.IsActive)
            {
                return OperationResult.Failure(
                    "Activate this product before publishing it - an inactive product cannot be sold.");
            }

            var priceListId = await DefaultPriceListIdAsync(cancellationToken);

            var pricedVariants = await _db.PriceListItems
                .Where(i => i.PriceListId == priceListId
                            && i.EffectiveToUtc == null
                            && i.ProductVariant!.ProductId == id
                            && i.ProductVariant.IsActive)
                .CountAsync(cancellationToken);

            if (pricedVariants == 0)
            {
                // A published product with no price renders as free or as
                // nothing, depending on the template. Neither is acceptable on
                // a live shop.
                return OperationResult.Failure(
                    "Set a price on at least one active variant before publishing.");
            }
        }

        product.IsPublished = published;
        product.PublishedAtUtc = published ? _clock.UtcNow : null;

        await _audit.LogAsync(
            published ? AuditActions.ProductPublished : AuditActions.ProductUnpublished,
            nameof(Product),
            product.Id.ToString(CultureInfo.InvariantCulture),
            published
                ? $"Published {product.Code} - {product.Name} to the storefront."
                : $"Removed {product.Code} - {product.Name} from the storefront.",
            new { product.Code, product.Name },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Soft delete. Allowed only while the product has never been priced -
    /// anything with price history has almost certainly been bought or sold,
    /// and deactivating is the honest way to retire it.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return OperationResult.Failure("That product no longer exists.");
        }

        var hasPriceHistory = await _db.PriceListItems
            .AnyAsync(i => i.ProductVariant!.ProductId == id, cancellationToken);

        if (hasPriceHistory)
        {
            return OperationResult.Failure(
                "This product has been priced, so it may already appear on documents. "
                + "Deactivate it instead - that hides it everywhere without breaking its history.");
        }

        product.IsDeleted = true;
        product.IsActive = false;
        product.IsPublished = false;

        foreach (var variant in product.Variants)
        {
            variant.IsActive = false;
        }

        await _audit.LogAsync(
            AuditActions.ProductUpdated,
            nameof(Product),
            product.Id.ToString(CultureInfo.InvariantCulture),
            $"Deleted product {product.Code} - {product.Name}.",
            new { product.Code, product.Name, Deleted = true },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<IReadOnlyList<UnitOption>> GetUnitOptionsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.UnitsOfMeasure
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .Select(u => new UnitOption { Id = u.Id, Code = u.Code, Name = u.Name })
            .ToListAsync(cancellationToken);

    private async Task<OperationResult> ApplyCategoriesAsync(
        Product product,
        SaveProductRequest request,
        CancellationToken cancellationToken)
    {
        var requested = request.CategoryIds.Distinct().ToList();

        if (requested.Count == 0)
        {
            // Not fatal - a product with no category simply cannot be browsed
            // to. It is refused because the alternative is a product nobody can
            // find and nobody knows is missing.
            return OperationResult.Failure(
                "Choose at least one category so the product can be found.",
                nameof(SaveProductRequest.CategoryIds));
        }

        var primary = request.PrimaryCategoryId ?? requested[0];

        if (!requested.Contains(primary))
        {
            return OperationResult.Failure(
                "The primary category has to be one of the selected categories.",
                nameof(SaveProductRequest.PrimaryCategoryId));
        }

        var existing = await _db.Categories
            .Where(c => requested.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (existing.Count != requested.Count)
        {
            return OperationResult.Failure("One of the selected categories no longer exists.");
        }

        var links = await _db.ProductCategories
            .Where(pc => pc.ProductId == product.Id)
            .ToListAsync(cancellationToken);

        foreach (var link in links.Where(l => !requested.Contains(l.CategoryId)))
        {
            _db.ProductCategories.Remove(link);
        }

        // The primary flag is cleared on every remaining row before the new one
        // is set: the filtered unique index rejects two primaries, and EF sends
        // these in one batch with no ordering guarantee between them.
        foreach (var link in links.Where(l => requested.Contains(l.CategoryId)))
        {
            link.IsPrimary = false;
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var categoryId in requested.Where(id => links.All(l => l.CategoryId != id)))
        {
            _db.ProductCategories.Add(new ProductCategory
            {
                ProductId = product.Id,
                CategoryId = categoryId,
                IsPrimary = false,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var primaryLink = await _db.ProductCategories
            .FirstAsync(pc => pc.ProductId == product.Id && pc.CategoryId == primary, cancellationToken);

        primaryLink.IsPrimary = true;

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private async Task<OperationResult> ValidateProductAsync(
        SaveProductRequest request,
        long? existingId,
        CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code) || !CodePattern().IsMatch(code))
        {
            return OperationResult.Failure(
                "Use 2 to 40 characters: letters, numbers, dashes and dots.",
                nameof(SaveProductRequest.Code));
        }

        if (await _db.Products.AnyAsync(
                p => p.Code == code && (existingId == null || p.Id != existingId), cancellationToken))
        {
            return OperationResult.Failure(
                $"Product code '{code}' is already in use.", nameof(SaveProductRequest.Code));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult.Failure("Enter a product name.", nameof(SaveProductRequest.Name));
        }

        if (!await _db.Brands.AnyAsync(b => b.Id == request.BrandId, cancellationToken))
        {
            return OperationResult.Failure("Choose a brand.", nameof(SaveProductRequest.BrandId));
        }

        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.UnitOfMeasureId, cancellationToken))
        {
            return OperationResult.Failure(
                "Choose a unit of measure.", nameof(SaveProductRequest.UnitOfMeasureId));
        }

        if (request.ShelfLifeDays is < 1 or > 3650)
        {
            return OperationResult.Failure(
                "Shelf life must be between 1 and 3650 days, or left blank.",
                nameof(SaveProductRequest.ShelfLifeDays));
        }

        return OperationResult.Success();
    }

    private async Task<OperationResult<string>> ResolveProductSlugAsync(
        string name,
        string? typedSlug,
        long? existingId,
        CancellationToken cancellationToken)
    {
        var taken = (await _db.Products
                .Where(p => existingId == null || p.Id != existingId)
                .Select(p => p.Slug)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(typedSlug))
        {
            var typed = typedSlug.Trim().ToLowerInvariant();

            if (!Slug.IsValid(typed))
            {
                return OperationResult<string>.Failure(
                    "Use lowercase letters, numbers and single hyphens.");
            }

            return taken.Contains(typed)
                ? OperationResult<string>.Failure($"The address '{typed}' is already used by another product.")
                : OperationResult<string>.Success(typed);
        }

        var generated = Slug.From(name);

        return string.IsNullOrEmpty(generated)
            ? OperationResult<string>.Failure(
                "Enter a web address for this product - one could not be made from the name.")
            : OperationResult<string>.Success(Slug.MakeUnique(generated, taken.Contains));
    }

    /// <summary>
    /// Next code in the P-000001 series.
    ///
    /// Deliberately not a database sequence: codes are only ever generated when
    /// the user leaves the field blank, a gap is harmless, and a sequence would
    /// be one more object to remember in every deployment.
    /// </summary>
    private async Task<string> NextProductCodeAsync(CancellationToken cancellationToken)
    {
        var generated = await _db.Products
            .IgnoreQueryFilters()
            .Where(p => p.Code.StartsWith("P-"))
            .Select(p => p.Code)
            .ToListAsync(cancellationToken);

        var highest = generated
            .Select(c => c.Length == 8 && int.TryParse(c[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"P-{highest + 1:D6}";
    }

    private async Task<long?> DefaultUnitIdAsync(CancellationToken cancellationToken) =>
        await _db.UnitsOfMeasure
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .Select(u => (long?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<long> DefaultPriceListIdAsync(CancellationToken cancellationToken) =>
        await _db.PriceLists
            .Where(p => p.IsDefault)
            .Select(p => p.Id)
            .FirstAsync(cancellationToken);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
