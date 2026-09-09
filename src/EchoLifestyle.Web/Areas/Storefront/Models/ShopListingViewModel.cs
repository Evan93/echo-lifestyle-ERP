using EchoLifestyle.Application.Storefront;

namespace EchoLifestyle.Web.Areas.Storefront.Models;

/// <summary>
/// One product grid, whatever produced it.
///
/// Category, brand and search pages differ in their heading and in the route
/// their pager and sort links point at. Everything else about them is
/// identical, so they share this and one partial rather than three views that
/// drift apart.
/// </summary>
public class ShopListingViewModel
{
    public string Heading { get; set; } = string.Empty;

    public string? Subheading { get; set; }

    public string? Description { get; set; }

    public string? ImagePath { get; set; }

    /// <summary>Sub-categories to browse into. Empty for brands and search.</summary>
    public IReadOnlyList<ShopCategory> Children { get; set; } = [];

    public ShopProductPage Products { get; set; } = new();

    public ShopSort Sort { get; set; }

    /// <summary>
    /// The current sort as it appears in a URL. On the model rather than the
    /// controller because the grid partial needs it to rebuild every pager and
    /// sort link, and a view cannot reach a protected helper.
    /// </summary>
    public string SortKey => Sort switch
    {
        ShopSort.PriceLowToHigh => "price-asc",
        ShopSort.PriceHighToLow => "price-desc",
        ShopSort.NameAToZ => "name",
        _ => "new",
    };

    /// <summary>The search term, echoed back so the pager keeps it.</summary>
    public string? Query { get; set; }

    /// <summary>Named route the pager and sort links rebuild.</summary>
    public string RouteName { get; set; } = "storefront-category";

    public string? RouteSlug { get; set; }

    public ShopFilters Filters { get; set; } = new();

    public ShopFacets Facets { get; set; } = new();

    /// <summary>
    /// Route values for a link that changes only sort or page.
    ///
    /// Every filter is carried through, because a pager that quietly drops them
    /// takes the shopper from "page 2 of CeraVe under 1500" to "page 2 of
    /// everything" without saying so.
    /// </summary>
    public object RouteValues(string? sort, int? page) => new
    {
        slug = RouteSlug,
        q = Query,
        sort,
        page,
        brand = Filters.BrandIds.ToArray(),
        min = Filters.MinPrice,
        max = Filters.MaxPrice,
        stock = Filters.InStockOnly ? "1" : null,
        offer = Filters.OnOfferOnly ? "1" : null,
    };

    /// <summary>The same listing with one brand toggled on or off.</summary>
    public object RouteValuesToggleBrand(long brandId)
    {
        var brands = Filters.BrandIds.Contains(brandId)
            ? Filters.BrandIds.Where(id => id != brandId).ToArray()
            : Filters.BrandIds.Append(brandId).ToArray();

        return new
        {
            slug = RouteSlug,
            q = Query,
            sort = SortKey,
            brand = brands,
            min = Filters.MinPrice,
            max = Filters.MaxPrice,
            stock = Filters.InStockOnly ? "1" : null,
            offer = Filters.OnOfferOnly ? "1" : null,
        };
    }

    /// <summary>The same listing with one toggle flipped and everything else kept.</summary>
    public object RouteValuesToggle(string flag) => new
    {
        slug = RouteSlug,
        q = Query,
        sort = SortKey,
        brand = Filters.BrandIds.ToArray(),
        min = Filters.MinPrice,
        max = Filters.MaxPrice,
        stock = flag == "stock" ? (Filters.InStockOnly ? null : "1") : (Filters.InStockOnly ? "1" : null),
        offer = flag == "offer" ? (Filters.OnOfferOnly ? null : "1") : (Filters.OnOfferOnly ? "1" : null),
    };

    /// <summary>Everything cleared, which is the way back out of a dead end.</summary>
    public object RouteValuesCleared() => new { slug = RouteSlug, q = Query, sort = SortKey };
}
