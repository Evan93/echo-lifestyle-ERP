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

    /// <summary>Route values for a link that changes only sort or page.</summary>
    public object RouteValues(string? sort, int? page) => new
    {
        slug = RouteSlug,
        q = Query,
        sort,
        page,
    };
}
