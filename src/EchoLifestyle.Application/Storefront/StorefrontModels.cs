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

/// <summary>
/// What a shopper has narrowed a listing down to.
///
/// Everything here comes from the query string and goes back into it, so a
/// filtered view is a real URL: shareable, bookmarkable, and the back button
/// works. Filtering that lives only in the browser cannot do any of that.
/// </summary>
public class ShopFilters
{
    /// <summary>Brands ticked in the sidebar. Empty means all of them.</summary>
    public IReadOnlyList<long> BrandIds { get; set; } = [];

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    /// <summary>Hides what cannot be bought today.</summary>
    public bool InStockOnly { get; set; }

    /// <summary>Only things genuinely marked down.</summary>
    public bool OnOfferOnly { get; set; }

    public bool Any =>
        BrandIds.Count > 0 || MinPrice is not null || MaxPrice is not null
        || InStockOnly || OnOfferOnly;

    public int Count =>
        BrandIds.Count
        + (MinPrice is not null || MaxPrice is not null ? 1 : 0)
        + (InStockOnly ? 1 : 0)
        + (OnOfferOnly ? 1 : 0);
}

/// <summary>
/// What is worth offering to filter by on this particular listing.
///
/// Computed from the products in scope <em>before</em> the brand filter is
/// applied, so ticking one brand does not make the others vanish from the
/// sidebar - which would leave somebody unable to widen their own search
/// without hitting back.
/// </summary>
public class ShopFacets
{
    public IReadOnlyList<BrandFacet> Brands { get; set; } = [];

    public decimal? LowestPrice { get; set; }

    public decimal? HighestPrice { get; set; }

    public bool HasAnything => Brands.Count > 1 || LowestPrice != HighestPrice;
}

public class BrandFacet
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>How many products carry it here. A count of zero is not listed.</summary>
    public int Count { get; set; }
}

/// <summary>
/// What belongs in sitemap.xml: only what the catalogue would show anyway, and
/// only categories and brands that actually have something under them.
/// </summary>
public class ShopSitemap
{
    public IReadOnlyList<SitemapEntry> Products { get; set; } = [];

    public IReadOnlyList<SitemapEntry> Categories { get; set; } = [];

    public IReadOnlyList<SitemapEntry> Brands { get; set; } = [];

    public int Count => Products.Count + Categories.Count + Brands.Count;
}

public class SitemapEntry
{
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Honest, or absent. A lastmod of "today" on every URL is worse than none:
    /// once a crawler learns the date means nothing it stops reading it, and
    /// the one time something really did change goes unnoticed.
    /// </summary>
    public DateTime LastModifiedUtc { get; set; }
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

    /// <summary>
    /// The brands that actually have something sellable in this category, most
    /// stocked first. Populated for the menu only.
    ///
    /// Worth stating why this is on the category rather than fetched beside it:
    /// a menu panel with one column of sub-categories is thin while the tree is
    /// shallow, and brand is how people who know what they want actually shop
    /// cosmetics. The count is the number of products this shopper would land
    /// on, so the panel cannot promise a number the page then contradicts.
    /// </summary>
    public IReadOnlyList<BrandFacet> Brands { get; set; } = [];
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

/// <summary>
/// The banner across the top of the home page, as a customer sees it.
///
/// Here with the other Shop* models rather than in Marketing, because this is
/// the customer's view of a banner and those are the back office's. They are
/// allowed to diverge - the admin one carries a name and a schedule that no
/// visitor should ever see.
/// </summary>
public class ShopBanner
{
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>A taller crop for phones. Null means the wide one serves both.</summary>
    public string? MobileImagePath { get; set; }

    public string AltText { get; set; } = string.Empty;

    public string? Headline { get; set; }

    public string? Subheading { get; set; }

    public string? LinkUrl { get; set; }

    public string? ButtonText { get; set; }

    /// <summary>
    /// True when there is text to lay over the image. A banner whose artwork
    /// already carries its own words gets no overlay and no scrim, because
    /// darkening a picture to make room for text that is not there just makes
    /// the picture worse.
    /// </summary>
    public bool HasOverlay =>
        !string.IsNullOrWhiteSpace(Headline) || !string.IsNullOrWhiteSpace(Subheading);
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
