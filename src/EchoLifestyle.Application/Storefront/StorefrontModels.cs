namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// One product as a grid tile.
///
/// Deliberately flat and deliberately small: a category page renders forty of
/// these, and every field on it is a field the database has to return forty
/// times.
/// </summary>
public class ShopProductCard
{
    public long ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public string BrandSlug { get; set; } = string.Empty;

    public string? ImagePath { get; set; }

    public string? ImageAlt { get; set; }

    /// <summary>
    /// The lowest current price across the product's sellable variants. Null
    /// when nothing has been priced, which is a merchandising mistake rather
    /// than a state customers should see - such products are filtered out.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// The struck-through "was" figure, when the variant carries one. Only
    /// shown when it is genuinely higher than the price.
    /// </summary>
    public decimal? CompareAtPrice { get; set; }

    /// <summary>True when variants differ in price, so the tile reads "From ৳X".</summary>
    public bool PriceVaries { get; set; }

    /// <summary>
    /// On hand minus reserved, across every sellable variant (rule 18). Stock
    /// already in somebody else's box is not for sale.
    /// </summary>
    public decimal Available { get; set; }

    public bool InStock => Available > 0m;

    public int VariantCount { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public bool IsDiscounted =>
        Price is not null && CompareAtPrice is not null && CompareAtPrice > Price;

    public int DiscountPercent => IsDiscounted
        ? (int)Math.Round((CompareAtPrice!.Value - Price!.Value) / CompareAtPrice.Value * 100m)
        : 0;
}

/// <summary>A page of tiles, plus what the pager needs.</summary>
public class ShopProductPage
{
    public IReadOnlyList<ShopProductCard> Products { get; set; } = [];

    public int TotalCount { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 24;

    public int TotalPages => PageSize <= 0
        ? 1
        : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

/// <summary>How a listing is ordered. Bound from the query string, so unknown values fall back.</summary>
public enum ShopSort
{
    Newest = 0,
    PriceLowToHigh = 1,
    PriceHighToLow = 2,
    NameAToZ = 3,
}

public class ShopCategory
{
    public long Id { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ImagePath { get; set; }

    public int Depth { get; set; }

    /// <summary>Materialised path from Phase 2 - what makes "everything under here" one index seek.</summary>
    public string Path { get; set; } = string.Empty;

    public IReadOnlyList<ShopCategory> Children { get; set; } = [];
}

public class ShopBrand
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? OriginCountry { get; set; }

    public string? LogoPath { get; set; }

    public string? BannerPath { get; set; }
}

/// <summary>What the home page needs, in one call.</summary>
public class ShopHome
{
    public IReadOnlyList<ShopBrand> FeaturedBrands { get; set; } = [];

    public IReadOnlyList<ShopCategory> Categories { get; set; } = [];

    public IReadOnlyList<ShopProductCard> NewArrivals { get; set; } = [];

    public IReadOnlyList<ShopProductCard> OnOffer { get; set; } = [];
}

/// <summary>One product page.</summary>
public class ShopProductDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    public string? LongDescription { get; set; }

    public string? HowToUse { get; set; }

    public string? Ingredients { get; set; }

    public ShopBrand Brand { get; set; } = new();

    /// <summary>Primary category first - what the breadcrumb follows.</summary>
    public IReadOnlyList<ShopCategory> Breadcrumb { get; set; } = [];

    public IReadOnlyList<ShopImage> Images { get; set; } = [];

    public IReadOnlyList<ShopOption> Options { get; set; } = [];

    public IReadOnlyList<ShopVariant> Variants { get; set; } = [];

    /// <summary>Null when nothing on the page is priced. Min over an empty set is null.</summary>
    public decimal? PriceFrom => Variants.Min(v => v.Price);

    public bool InStock => Variants.Any(v => v.InStock);

    /// <summary>
    /// The variant a page opens on: the first one actually purchasable, or the
    /// default when everything is out of stock. Landing on an unavailable shade
    /// when another is on the shelf loses the sale for no reason.
    /// </summary>
    public ShopVariant? OpeningVariant =>
        Variants.FirstOrDefault(v => v.InStock && v.Price is not null)
        ?? Variants.FirstOrDefault(v => v.IsDefault)
        ?? Variants.FirstOrDefault();
}

public class ShopImage
{
    public long Id { get; set; }

    /// <summary>Null for the general shots; set for a shade's own photograph.</summary>
    public long? ProductVariantId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string AltText { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
}

public class ShopOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyList<ShopOptionValue> Values { get; set; } = [];
}

public class ShopOptionValue
{
    public long Id { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>What lets a shade render as a circle instead of a dropdown row.</summary>
    public string? SwatchHex { get; set; }
}

public class ShopVariant
{
    public long Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public decimal Available { get; set; }

    public bool IsDefault { get; set; }

    public bool InStock => Available > 0m && Price is not null;

    /// <summary>Option value ids this variant is, so the page can match a selection to it.</summary>
    public IReadOnlyList<long> OptionValueIds { get; set; } = [];

    public bool IsDiscounted =>
        Price is not null && CompareAtPrice is not null && CompareAtPrice > Price;
}
