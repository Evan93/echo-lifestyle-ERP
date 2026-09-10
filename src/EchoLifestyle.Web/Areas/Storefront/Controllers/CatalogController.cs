using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Web.Areas.Storefront.Models;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// Browsing: categories, brands, one product, and search.
///
/// Every listing shares one view model and one grid partial, because a category
/// page and a brand page differ only in their heading. Two nearly-identical
/// views is how one of them quietly stops matching the other.
/// </summary>
public class CatalogController : StorefrontControllerBase
{
    private readonly StorefrontCatalogService _catalog;

    public CatalogController(StorefrontCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> Category(
        string slug,
        string? sort,
        int? page,
        [FromQuery(Name = "brand")] long[]? brands,
        [FromQuery(Name = "min")] decimal? minPrice,
        [FromQuery(Name = "max")] decimal? maxPrice,
        [FromQuery(Name = "stock")] string? inStock,
        [FromQuery(Name = "offer")] string? onOffer,
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);
        var filters = ParseFilters(brands, minPrice, maxPrice, inStock, onOffer);

        var (category, products, facets) = await _catalog.GetCategoryAsync(
            slug, order, ParsePage(page), filters, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        ViewData["Title"] = category.Name;
        ViewData["MetaDescription"] = category.Description;

        return View(new ShopListingViewModel
        {
            Heading = category.Name,
            Description = category.Description,
            ImagePath = category.ImagePath,
            Children = category.Children,
            Products = products,
            Sort = order,
            Filters = filters,
            Facets = facets,
            RouteName = "storefront-category",
            RouteSlug = category.Slug,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Brand(
        string slug,
        string? sort,
        int? page,
        [FromQuery(Name = "min")] decimal? minPrice,
        [FromQuery(Name = "max")] decimal? maxPrice,
        [FromQuery(Name = "stock")] string? inStock,
        [FromQuery(Name = "offer")] string? onOffer,
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);

        // No brand facet on a brand page - everything here is that brand.
        var filters = ParseFilters(null, minPrice, maxPrice, inStock, onOffer);

        var (brand, products, facets) = await _catalog.GetBrandAsync(
            slug, order, ParsePage(page), filters, cancellationToken);

        if (brand is null)
        {
            return NotFound();
        }

        ViewData["Title"] = brand.Name;
        ViewData["MetaDescription"] = brand.Description;

        return View(new ShopListingViewModel
        {
            Heading = brand.Name,
            Subheading = brand.OriginCountry,
            Description = brand.Description,
            ImagePath = brand.BannerPath ?? brand.LogoPath,
            Products = products,
            Sort = order,
            Filters = filters,
            Facets = new ShopFacets
            {
                LowestPrice = facets.LowestPrice,
                HighestPrice = facets.HighestPrice,
            },
            RouteName = "storefront-brand",
            RouteSlug = brand.Slug,
        });
    }

    /// <summary>
    /// The fixed menu entries: everything new, and everything on offer.
    ///
    /// Both render through the category view, because a listing is a listing -
    /// a near-identical second view is how one of them quietly stops matching
    /// the other. Only the heading and the starting sort differ.
    /// </summary>
    [HttpGet]
    public Task<IActionResult> New(
        string? sort,
        int? page,
        [FromQuery(Name = "brand")] long[]? brands,
        [FromQuery(Name = "min")] decimal? minPrice,
        [FromQuery(Name = "max")] decimal? maxPrice,
        [FromQuery(Name = "stock")] string? inStock,
        CancellationToken cancellationToken) =>
        ListingAsync(
            heading: "New in",
            description: "The most recent additions to the shop.",
            routeName: "storefront-new",
            forceOffers: false,
            sort: sort,
            page: page,
            brands: brands,
            minPrice: minPrice,
            maxPrice: maxPrice,
            inStock: inStock,
            cancellationToken: cancellationToken);

    [HttpGet]
    public Task<IActionResult> Offers(
        string? sort,
        int? page,
        [FromQuery(Name = "brand")] long[]? brands,
        [FromQuery(Name = "min")] decimal? minPrice,
        [FromQuery(Name = "max")] decimal? maxPrice,
        [FromQuery(Name = "stock")] string? inStock,
        CancellationToken cancellationToken) =>
        ListingAsync(
            heading: "On offer",
            description: "Everything currently marked down. Prices shown are what you pay.",
            routeName: "storefront-offers",

            // Not a filter the visitor can untick here: an offers page that can
            // be switched to show non-offers is just the shop with a misleading
            // heading on it.
            forceOffers: true,
            sort: sort,
            page: page,
            brands: brands,
            minPrice: minPrice,
            maxPrice: maxPrice,
            inStock: inStock,
            cancellationToken: cancellationToken);

    private async Task<IActionResult> ListingAsync(
        string heading,
        string description,
        string routeName,
        bool forceOffers,
        string? sort,
        int? page,
        long[]? brands,
        decimal? minPrice,
        decimal? maxPrice,
        string? inStock,
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);
        var filters = ParseFilters(brands, minPrice, maxPrice, inStock, forceOffers ? "1" : null);

        var (products, facets) = await _catalog.GetAllAsync(
            order, ParsePage(page), filters, cancellationToken);

        ViewData["Title"] = heading;
        ViewData["MetaDescription"] = description;

        return View("Category", new ShopListingViewModel
        {
            Heading = heading,
            Description = description,
            Products = products,
            Sort = order,
            Filters = filters,
            Facets = facets,
            RouteName = routeName,
            ShowOfferFilter = !forceOffers,
        });
    }

    /// <summary>
    /// The brand index. A flat A-to-Z, because a shopper who arrives looking
    /// for a brand knows its name and wants to find it, not to browse.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Brands(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Brands";
        ViewData["MetaDescription"] =
            "Every brand stocked at Echo Lifestyle, sourced genuine and delivered across Bangladesh.";

        return View(await _catalog.GetBrandIndexAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        string? sort,
        int? page,
        [FromQuery(Name = "brand")] long[]? brands,
        [FromQuery(Name = "min")] decimal? minPrice,
        [FromQuery(Name = "max")] decimal? maxPrice,
        [FromQuery(Name = "stock")] string? inStock,
        [FromQuery(Name = "offer")] string? onOffer,
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);
        var filters = ParseFilters(brands, minPrice, maxPrice, inStock, onOffer);

        var (products, facets) = await _catalog.SearchAsync(
            q, order, ParsePage(page), filters, cancellationToken);

        ViewData["Title"] = string.IsNullOrWhiteSpace(q) ? "Search" : $"Search: {q}";

        // Search results are not something to index - they are a different page
        // for every visitor and dilute the pages that do matter.
        ViewData["NoIndex"] = true;

        return View(new ShopListingViewModel
        {
            Heading = string.IsNullOrWhiteSpace(q) ? "Search" : $"Results for “{q}”",
            Products = products,
            Sort = order,
            Query = q,
            Filters = filters,
            Facets = facets,
            RouteName = "storefront-search",
        });
    }

    /// <summary>
    /// Filters out of the query string, forgiving rather than strict.
    ///
    /// A public URL gets mistyped, truncated and pasted half-formed. Nothing
    /// here throws: a nonsense value is simply not a filter.
    /// </summary>
    private static ShopFilters ParseFilters(
        long[]? brands,
        decimal? minPrice,
        decimal? maxPrice,
        string? inStock,
        string? onOffer)
    {
        // A reversed range is what somebody typing 2000 into "from" and 500
        // into "to" means, not an empty result.
        if (minPrice is not null && maxPrice is not null && minPrice > maxPrice)
        {
            (minPrice, maxPrice) = (maxPrice, minPrice);
        }

        return new ShopFilters
        {
            // Capped, because a crafted URL should not be able to ask for a
            // thousand-item IN clause.
            BrandIds = brands?.Where(id => id > 0).Distinct().Take(20).ToList() ?? [],
            MinPrice = minPrice is > 0m ? minPrice : null,
            MaxPrice = maxPrice is > 0m ? maxPrice : null,
            InStockOnly = inStock == "1",
            OnOfferOnly = onOffer == "1",
        };
    }

    [HttpGet]
    public async Task<IActionResult> Product(string slug, CancellationToken cancellationToken)
    {
        var product = await _catalog.GetProductAsync(slug, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        ViewData["Title"] = product.Name;
        ViewData["MetaDescription"] = product.ShortDescription;

        return View(product);
    }
}
