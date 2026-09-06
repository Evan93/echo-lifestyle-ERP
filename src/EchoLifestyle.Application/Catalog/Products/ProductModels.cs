namespace EchoLifestyle.Application.Catalog.Products;

public class ProductListItem
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public string? PrimaryCategoryName { get; set; }

    public int VariantCount { get; set; }

    /// <summary>
    /// Lowest and highest current selling price across the product's variants.
    /// Null when nothing has been priced yet, which is a state worth seeing in
    /// the grid rather than showing as zero.
    /// </summary>
    public decimal? PriceFrom { get; set; }

    public decimal? PriceTo { get; set; }

    public bool IsActive { get; set; }

    public bool IsPublished { get; set; }

    public bool IsBatchTracked { get; set; }
}

public class ProductDetail
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public long BrandId { get; set; }

    public string BrandName { get; set; } = string.Empty;

    public long UnitOfMeasureId { get; set; }

    public string? ShortDescription { get; set; }

    public string? LongDescription { get; set; }

    public string? HowToUse { get; set; }

    public string? Ingredients { get; set; }

    public bool IsBatchTracked { get; set; }

    public bool IsExpiryTracked { get; set; }

    public int? ShelfLifeDays { get; set; }

    public bool IsActive { get; set; }

    public bool IsPublished { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public IReadOnlyList<long> CategoryIds { get; set; } = [];

    public long? PrimaryCategoryId { get; set; }

    public IReadOnlyList<ProductOptionDetail> Options { get; set; } = [];

    public IReadOnlyList<ProductVariantDetail> Variants { get; set; } = [];
}

public class ProductOptionDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public IReadOnlyList<ProductOptionValueDetail> Values { get; set; } = [];

    /// <summary>The values as the editor shows them: "30ml, 50ml, 100ml".</summary>
    public string ValueList => string.Join(", ", Values.Select(v => v.ToEditorText()));
}

public class ProductOptionValueDetail
{
    public long Id { get; set; }

    public string Value { get; set; } = string.Empty;

    public string? SwatchHex { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>
    /// "Ruby Red #C21807" - the swatch travels with the value in the same text
    /// box, so defining eight shades stays one paste rather than eight rows.
    /// </summary>
    public string ToEditorText() =>
        string.IsNullOrEmpty(SwatchHex) ? Value : $"{Value} {SwatchHex}";
}

public class ProductVariantDetail
{
    public long Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string VariantName { get; set; } = string.Empty;

    public decimal? Mrp { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public decimal? WeightGrams { get; set; }

    public decimal? CurrentPrice { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>
    /// True when this variant's option combination no longer exists - it was
    /// retired by an options change rather than chosen. Shown so nobody
    /// wonders why an inactive row is there.
    /// </summary>
    public bool IsRetired { get; set; }
}

/// <summary>
/// The one-screen create path. Everything optional here has a defensible
/// default, so adding a product is name + brand + price and nothing else.
/// </summary>
public class QuickCreateProductRequest
{
    public string Name { get; set; } = string.Empty;

    public long BrandId { get; set; }

    public long? CategoryId { get; set; }

    public long? UnitOfMeasureId { get; set; }

    /// <summary>Blank generates the next sequential code.</summary>
    public string? Code { get; set; }

    /// <summary>Blank generates one from the product code.</summary>
    public string? Sku { get; set; }

    public string? Barcode { get; set; }

    public decimal? Price { get; set; }

    public decimal? Mrp { get; set; }

    public bool IsBatchTracked { get; set; }

    public bool IsExpiryTracked { get; set; }

    public bool IsActive { get; set; } = true;
}

public class SaveProductRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public long BrandId { get; set; }

    public long UnitOfMeasureId { get; set; }

    public string? ShortDescription { get; set; }

    public string? LongDescription { get; set; }

    public string? HowToUse { get; set; }

    public string? Ingredients { get; set; }

    public bool IsBatchTracked { get; set; }

    public bool IsExpiryTracked { get; set; }

    public int? ShelfLifeDays { get; set; }

    public bool IsActive { get; set; } = true;

    public IReadOnlyList<long> CategoryIds { get; set; } = [];

    public long? PrimaryCategoryId { get; set; }
}

/// <summary>One axis as the editor posts it: a name and a line of values.</summary>
public class OptionInput
{
    public string? Name { get; set; }

    /// <summary>
    /// Comma-separated, each optionally followed by a hex colour:
    /// "Ruby Red #C21807, Coral #FF6F61".
    /// </summary>
    public string? Values { get; set; }
}

public class SaveOptionsRequest
{
    public IReadOnlyList<OptionInput> Options { get; set; } = [];
}

/// <summary>One row of the variant table.</summary>
public class VariantInput
{
    public long Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public decimal? Price { get; set; }

    public decimal? Mrp { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public decimal? WeightGrams { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }
}

public class SaveVariantsRequest
{
    public IReadOnlyList<VariantInput> Variants { get; set; } = [];

    /// <summary>
    /// False when the caller lacks Catalog.Price.Edit. Prices posted by such a
    /// user are ignored rather than trusted - the form disables the field, and
    /// a disabled field is not a control anyone has to respect.
    /// </summary>
    public bool MayEditPrices { get; set; }
}

/// <summary>Summary of what an options change did, so the UI can say so plainly.</summary>
public class VariantRebuildSummary
{
    public int Created { get; set; }

    public int Retained { get; set; }

    public int Retired { get; set; }

    public int Removed { get; set; }

    public string Describe()
    {
        var parts = new List<string>();

        if (Created > 0)
        {
            parts.Add($"{Created} new variant{(Created == 1 ? "" : "s")}");
        }

        if (Retired > 0)
        {
            parts.Add($"{Retired} retired but kept (they have price history)");
        }

        if (Removed > 0)
        {
            parts.Add($"{Removed} removed");
        }

        return parts.Count == 0
            ? "Variants are unchanged."
            : "Variants rebuilt: " + string.Join(", ", parts) + ".";
    }
}

public class UnitOption
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
