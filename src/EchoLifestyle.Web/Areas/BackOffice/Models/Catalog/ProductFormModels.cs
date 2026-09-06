using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using Microsoft.AspNetCore.Http;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Catalog;

/// <summary>
/// The one-screen create form. Deliberately short: everything omitted has a
/// defensible default, and the full editor opens straight afterwards.
/// </summary>
public class QuickProductFormModel
{
    [Required(ErrorMessage = "Enter a product name.")]
    [StringLength(250)]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a brand.")]
    [Display(Name = "Brand")]
    public long BrandId { get; set; }

    [Display(Name = "Category")]
    public long? CategoryId { get; set; }

    [Display(Name = "Unit")]
    public long? UnitOfMeasureId { get; set; }

    [StringLength(40)]
    [Display(Name = "Product code")]
    public string? Code { get; set; }

    [StringLength(40)]
    [Display(Name = "SKU")]
    public string? Sku { get; set; }

    [StringLength(50)]
    [Display(Name = "Barcode")]
    public string? Barcode { get; set; }

    [Range(0, 99999999)]
    [Display(Name = "Selling price")]
    public decimal? Price { get; set; }

    [Range(0, 99999999)]
    [Display(Name = "MRP")]
    public decimal? Mrp { get; set; }

    [Display(Name = "Track batches")]
    public bool IsBatchTracked { get; set; }

    [Display(Name = "Track expiry dates")]
    public bool IsExpiryTracked { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<BrandOption> Brands { get; set; } = [];

    public IReadOnlyList<CategoryOption> Categories { get; set; } = [];

    public IReadOnlyList<UnitOption> Units { get; set; } = [];

    public QuickCreateProductRequest ToRequest() => new()
    {
        Name = Name,
        BrandId = BrandId,
        CategoryId = CategoryId,
        UnitOfMeasureId = UnitOfMeasureId,
        Code = Code,
        Sku = Sku,
        Barcode = Barcode,
        Price = Price,
        Mrp = Mrp,
        IsBatchTracked = IsBatchTracked,
        IsExpiryTracked = IsExpiryTracked,
        IsActive = IsActive,
    };
}

/// <summary>
/// The full editor. Rendered as one page with three independent forms - details,
/// options and variants - so a failure in one does not throw away what was typed
/// in the others.
/// </summary>
public class ProductFormModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Enter a product code.")]
    [StringLength(40, MinimumLength = 2)]
    [Display(Name = "Product code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a product name.")]
    [StringLength(250)]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(160)]
    [RegularExpression(
        "^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Use lowercase letters, numbers and single hyphens.")]
    [Display(Name = "Web address")]
    public string? Slug { get; set; }

    [Required(ErrorMessage = "Choose a brand.")]
    [Display(Name = "Brand")]
    public long BrandId { get; set; }

    [Required(ErrorMessage = "Choose a unit.")]
    [Display(Name = "Unit")]
    public long UnitOfMeasureId { get; set; }

    [StringLength(600)]
    [Display(Name = "Short description")]
    public string? ShortDescription { get; set; }

    [StringLength(8000)]
    [Display(Name = "Description")]
    public string? LongDescription { get; set; }

    [StringLength(4000)]
    [Display(Name = "How to use")]
    public string? HowToUse { get; set; }

    [StringLength(8000)]
    [Display(Name = "Ingredients")]
    public string? Ingredients { get; set; }

    [Display(Name = "Track batches")]
    public bool IsBatchTracked { get; set; }

    [Display(Name = "Track expiry dates")]
    public bool IsExpiryTracked { get; set; }

    [Range(1, 3650)]
    [Display(Name = "Shelf life (days)")]
    public int? ShelfLifeDays { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public bool IsPublished { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    [Display(Name = "Categories")]
    public List<long> CategoryIds { get; set; } = [];

    [Display(Name = "Primary category")]
    public long? PrimaryCategoryId { get; set; }

    public IReadOnlyList<BrandOption> Brands { get; set; } = [];

    public IReadOnlyList<CategoryOption> Categories { get; set; } = [];

    public IReadOnlyList<UnitOption> Units { get; set; } = [];

    public IReadOnlyList<ProductOptionDetail> Options { get; set; } = [];

    public IReadOnlyList<ProductVariantDetail> Variants { get; set; } = [];

    public bool MayEditPrices { get; set; }

    public bool MayPublish { get; set; }

    public bool HasPriceHistory { get; set; }

    public SaveProductRequest ToRequest() => new()
    {
        Code = Code,
        Name = Name,
        Slug = Slug,
        BrandId = BrandId,
        UnitOfMeasureId = UnitOfMeasureId,
        ShortDescription = ShortDescription,
        LongDescription = LongDescription,
        HowToUse = HowToUse,
        Ingredients = Ingredients,
        IsBatchTracked = IsBatchTracked,
        IsExpiryTracked = IsExpiryTracked,
        ShelfLifeDays = ShelfLifeDays,
        IsActive = IsActive,
        CategoryIds = CategoryIds,
        PrimaryCategoryId = PrimaryCategoryId,
    };

    public static ProductFormModel FromDetail(ProductDetail detail) => new()
    {
        Id = detail.Id,
        Code = detail.Code,
        Name = detail.Name,
        Slug = detail.Slug,
        BrandId = detail.BrandId,
        UnitOfMeasureId = detail.UnitOfMeasureId,
        ShortDescription = detail.ShortDescription,
        LongDescription = detail.LongDescription,
        HowToUse = detail.HowToUse,
        Ingredients = detail.Ingredients,
        IsBatchTracked = detail.IsBatchTracked,
        IsExpiryTracked = detail.IsExpiryTracked,
        ShelfLifeDays = detail.ShelfLifeDays,
        IsActive = detail.IsActive,
        IsPublished = detail.IsPublished,
        PublishedAtUtc = detail.PublishedAtUtc,
        CategoryIds = detail.CategoryIds.ToList(),
        PrimaryCategoryId = detail.PrimaryCategoryId,
        Options = detail.Options,
        Variants = detail.Variants,
    };
}

/// <summary>
/// The options form. Three fixed rows rather than an add/remove list: the
/// maximum is three, and a fixed shape needs no client-side row management at
/// all.
/// </summary>
public class OptionsFormModel
{
    public long ProductId { get; set; }

    public List<OptionRow> Rows { get; set; } =
    [
        new OptionRow(),
        new OptionRow(),
        new OptionRow(),
    ];

    public SaveOptionsRequest ToRequest() => new()
    {
        Options = Rows
            .Select(r => new OptionInput { Name = r.Name, Values = r.Values })
            .ToList(),
    };

    public static OptionsFormModel FromDetail(ProductDetail detail)
    {
        var model = new OptionsFormModel { ProductId = detail.Id };

        for (var index = 0; index < model.Rows.Count && index < detail.Options.Count; index++)
        {
            model.Rows[index].Name = detail.Options[index].Name;
            model.Rows[index].Values = detail.Options[index].ValueList;
        }

        return model;
    }
}

public class OptionRow
{
    [StringLength(50)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Values { get; set; }
}

/// <summary>
/// The three forms that make up the editor page.
///
/// Each half is bound on its own so a failure in one cannot discard what was
/// typed in another, and so every field posts under a plain name that matches
/// the action's parameter - which is what keeps validation messages attached to
/// the right input.
/// </summary>
public class ProductEditorViewModel
{
    public ProductFormModel Product { get; set; } = new();

    public OptionsFormModel Options { get; set; } = new();

    public VariantsFormModel Variants { get; set; } = new();

    public ImagesFormModel Images { get; set; } = new();
}

public class ImagesFormModel
{
    public long ProductId { get; set; }

    public List<ImageRow> Rows { get; set; } = [];

    /// <summary>Where a new upload can be attached: the product, or one variant.</summary>
    public IReadOnlyList<VariantRow> Galleries { get; set; } = [];

    public int MaxImages { get; set; }

    public SaveImagesRequest ToRequest() => new()
    {
        Images = Rows
            .Select(r => new ImageInput
            {
                Id = r.Id,
                AltText = r.AltText,
                DisplayOrder = r.DisplayOrder,
                IsPrimary = r.IsPrimary,
            })
            .ToList(),
    };

    public static ImagesFormModel FromDetail(
        long productId,
        IReadOnlyList<ProductImageDetail> images,
        IReadOnlyList<VariantRow> variants) => new()
        {
            ProductId = productId,
            MaxImages = ProductAdminService.MaxImagesPerProduct,
            Galleries = variants,
            Rows = images
                .Select(i => new ImageRow
                {
                    Id = i.Id,
                    ProductVariantId = i.ProductVariantId,
                    VariantName = i.VariantName,
                    Path = i.Path,
                    AltText = i.AltText,
                    DisplayOrder = i.DisplayOrder,
                    IsPrimary = i.IsPrimary,
                })
                .ToList(),
        };
}

public class ImageRow
{
    public long Id { get; set; }

    public long? ProductVariantId { get; set; }

    public string? VariantName { get; set; }

    /// <summary>Read-only: the path is decided by the file store, never posted.</summary>
    public string Path { get; set; } = string.Empty;

    [Required(ErrorMessage = "Every image needs alt text.")]
    [StringLength(250)]
    public string AltText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }
}

public class VariantsFormModel
{
    public long ProductId { get; set; }

    /// <summary>
    /// Display only - it decides whether the price column is enabled. The
    /// authoritative check is made in the controller from the signed-in user's
    /// permissions, never from anything posted.
    /// </summary>
    public bool MayEditPrices { get; set; }

    public List<VariantRow> Rows { get; set; } = [];

    public SaveVariantsRequest ToRequest(bool mayEditPrices) => new()
    {
        MayEditPrices = mayEditPrices,
        Variants = Rows
            .Select(r => new VariantInput
            {
                Id = r.Id,
                Sku = r.Sku,
                Barcode = r.Barcode,
                Price = r.Price,
                Mrp = r.Mrp,
                CompareAtPrice = r.CompareAtPrice,
                WeightGrams = r.WeightGrams,
                IsActive = r.IsActive,
                DisplayOrder = r.DisplayOrder,
            })
            .ToList(),
    };
}

public class VariantRow
{
    public long Id { get; set; }

    public string VariantName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Every variant needs an SKU.")]
    [StringLength(40, MinimumLength = 2)]
    public string Sku { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Barcode { get; set; }

    [Range(0, 99999999)]
    public decimal? Price { get; set; }

    [Range(0, 99999999)]
    public decimal? Mrp { get; set; }

    [Range(0, 99999999)]
    public decimal? CompareAtPrice { get; set; }

    [Range(0, 100000)]
    public decimal? WeightGrams { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsDefault { get; set; }

    public bool IsRetired { get; set; }
}
