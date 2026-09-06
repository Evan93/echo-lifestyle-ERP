using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Web.Areas.BackOffice.Models.Catalog;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Catalog.ProductView)]
public class ProductsController : BackOfficeControllerBase
{
    private readonly ProductAdminService _products;
    private readonly BrandAdminService _brands;
    private readonly CategoryAdminService _categories;
    private readonly ICurrentUser _currentUser;

    public ProductsController(
        ProductAdminService products,
        BrandAdminService brands,
        CategoryAdminService categories,
        ICurrentUser currentUser)
    {
        _products = products;
        _brands = brands;
        _categories = categories;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Brands"] = await _brands.GetOptionsAsync(cancellationToken: cancellationToken);
        ViewData["Categories"] = await _categories.GetAllOptionsAsync(cancellationToken);

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        long? brandId,
        long? categoryId,
        CancellationToken cancellationToken)
    {
        var page = await _products.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            brandId,
            categoryId,
            cancellationToken);

        return Json(GridResponse<ProductListItem>.From(request, page));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New product";

        var model = new QuickProductFormModel();
        await PopulateAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> Create(QuickProductFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New product";

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _products.QuickCreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        // Straight into the full editor rather than back to the list: quick
        // create exists to get the product into the system fast, and the next
        // thing anyone wants is to add its shades or its description.
        Notify($"'{model.Name}' created. Add options, images and detail here.");
        return RedirectToAction(nameof(Edit), new { id = result.Value });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _products.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        return View(await BuildEditorAsync(detail, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> Edit(long id, ProductFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;

        if (!ModelState.IsValid)
        {
            return View(await RehydrateAsync(model, cancellationToken));
        }

        var result = await _products.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View(await RehydrateAsync(model, cancellationToken));
        }

        Notify($"'{model.Name}' saved.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Rebuilding the variant matrix. Separate from the details form so a
    /// mistyped option list cannot discard a half-written description.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> SaveOptions(
        long id,
        OptionsFormModel model,
        CancellationToken cancellationToken)
    {
        var result = await _products.SaveOptionsAsync(id, model.ToRequest(), cancellationToken);

        Notify(
            result.Succeeded ? result.Value!.Describe() : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> SaveVariants(
        long id,
        VariantsFormModel model,
        CancellationToken cancellationToken)
    {
        // Whether prices may be changed is decided here, from the signed-in
        // user's permissions - never from anything the form posted.
        var mayEditPrices = _currentUser.HasPermission(Permissions.Catalog.PriceEdit);

        var result = await _products.SaveVariantsAsync(id, model.ToRequest(mayEditPrices), cancellationToken);

        Notify(
            result.Succeeded ? "Variants saved." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Uploads one or more images into the product's gallery, or into one
    /// variant's.
    ///
    /// Nothing the browser sends decides the file name, the extension or the
    /// location: the controller maps the declared content type onto a closed set
    /// of kinds, and the file store proves the bytes match before writing
    /// anything.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> UploadImages(
        long id,
        List<IFormFile> files,
        long? variantId,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            Notify("Choose at least one image.", "warning");
            return RedirectToAction(nameof(Edit), new { id });
        }

        var added = 0;
        var failures = new List<string>();

        foreach (var file in files)
        {
            var kind = MapKind(file.ContentType);

            if (kind is null)
            {
                failures.Add($"{file.FileName}: only JPEG, PNG and WebP images are accepted.");
                continue;
            }

            await using var content = file.OpenReadStream();

            var result = await _products.AddImageAsync(
                id,
                new UploadImageRequest
                {
                    Content = content,
                    Kind = kind.Value,
                    ProductVariantId = variantId,
                },
                cancellationToken);

            if (result.Succeeded)
            {
                added++;
            }
            else
            {
                failures.Add($"{file.FileName}: {result.Error}");
            }
        }

        // Reported together: uploading eight shades and having one rejected
        // should say which one, not fail the batch.
        if (failures.Count > 0)
        {
            Notify(
                (added > 0 ? $"{added} image(s) added. " : string.Empty) + string.Join(" ", failures),
                added > 0 ? "warning" : "danger");
        }
        else
        {
            Notify($"{added} image(s) added.");
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> SaveImages(
        long id,
        ImagesFormModel model,
        CancellationToken cancellationToken)
    {
        var result = await _products.SaveImagesAsync(id, model.ToRequest(), cancellationToken);

        Notify(
            result.Succeeded ? "Images saved." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> DeleteImage(long id, long imageId, CancellationToken cancellationToken)
    {
        var result = await _products.DeleteImageAsync(id, imageId, cancellationToken);

        Notify(
            result.Succeeded ? "Image removed." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Maps a declared content type onto the closed set of kinds the store
    /// accepts. The header is a claim, not evidence - the store verifies the
    /// bytes - but an unrecognised claim is refused here so the file is never
    /// read at all.
    /// </summary>
    private static FileKind? MapKind(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" or "image/pjpeg" => FileKind.Jpeg,
        "image/png" => FileKind.Png,
        "image/webp" => FileKind.WebP,
        _ => null,
    };

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductPublish)]
    public async Task<IActionResult> Publish(long id, bool publish, CancellationToken cancellationToken)
    {
        var result = await _products.SetPublishedAsync(id, publish, cancellationToken);

        Notify(
            result.Succeeded
                ? (publish ? "Product published to the storefront." : "Product removed from the storefront.")
                : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.ProductEdit)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var result = await _products.DeleteAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Edit), new { id });
        }

        Notify("Product deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<ProductEditorViewModel> BuildEditorAsync(
        ProductDetail detail,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = detail.Name;

        var model = ProductFormModel.FromDetail(detail);
        await PopulateAsync(model, cancellationToken);

        var variantRows = detail.Variants
            .Select(v => new VariantRow
            {
                Id = v.Id,
                VariantName = v.VariantName,
                Sku = v.Sku,
                Barcode = v.Barcode,
                Price = v.CurrentPrice,
                Mrp = v.Mrp,
                CompareAtPrice = v.CompareAtPrice,
                WeightGrams = v.WeightGrams,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
                IsDefault = v.IsDefault,
                IsRetired = v.IsRetired,
            })
            .ToList();

        var images = await _products.GetImagesAsync(detail.Id, cancellationToken);

        return new ProductEditorViewModel
        {
            Product = model,
            Options = OptionsFormModel.FromDetail(detail),
            Variants = new VariantsFormModel
            {
                ProductId = detail.Id,
                MayEditPrices = model.MayEditPrices,
                Rows = variantRows,
            },
            Images = ImagesFormModel.FromDetail(
                detail.Id,
                images,
                variantRows.Where(v => !v.IsRetired).ToList()),
        };
    }

    /// <summary>
    /// Rebuilds the editor around a details form that failed to save, keeping
    /// what the user typed while re-reading everything the form does not post.
    /// </summary>
    private async Task<ProductEditorViewModel> RehydrateAsync(
        ProductFormModel model,
        CancellationToken cancellationToken)
    {
        var detail = await _products.GetAsync(model.Id, cancellationToken);

        if (detail is null)
        {
            await PopulateAsync(model, cancellationToken);
            return new ProductEditorViewModel { Product = model };
        }

        var editor = await BuildEditorAsync(detail, cancellationToken);

        // The typed values win; the read-only parts come from the database.
        model.Brands = editor.Product.Brands;
        model.Categories = editor.Product.Categories;
        model.Units = editor.Product.Units;
        model.Options = editor.Product.Options;
        model.Variants = editor.Product.Variants;
        model.IsPublished = editor.Product.IsPublished;
        model.PublishedAtUtc = editor.Product.PublishedAtUtc;
        model.MayEditPrices = editor.Product.MayEditPrices;
        model.MayPublish = editor.Product.MayPublish;
        model.HasPriceHistory = editor.Product.HasPriceHistory;

        editor.Product = model;

        return editor;
    }

    private async Task PopulateAsync(QuickProductFormModel model, CancellationToken cancellationToken)
    {
        model.Brands = await _brands.GetOptionsAsync(cancellationToken: cancellationToken);
        model.Categories = await _categories.GetAllOptionsAsync(cancellationToken);
        model.Units = await _products.GetUnitOptionsAsync(cancellationToken);
    }

    private async Task PopulateAsync(ProductFormModel model, CancellationToken cancellationToken)
    {
        // The current brand is included even when inactive: omitting it would
        // silently move the product to whichever brand came first in the list.
        model.Brands = await _brands.GetOptionsAsync(model.BrandId, cancellationToken);
        model.Categories = await _categories.GetAllOptionsAsync(cancellationToken);
        model.Units = await _products.GetUnitOptionsAsync(cancellationToken);
        model.MayEditPrices = _currentUser.HasPermission(Permissions.Catalog.PriceEdit);
        model.MayPublish = _currentUser.HasPermission(Permissions.Catalog.ProductPublish);
        model.HasPriceHistory = model.Variants.Any(v => v.CurrentPrice is not null);
    }
}
