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
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);

        var (category, products) = await _catalog.GetCategoryAsync(
            slug, order, ParsePage(page), cancellationToken);

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
            RouteName = "storefront-category",
            RouteSlug = category.Slug,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Brand(
        string slug,
        string? sort,
        int? page,
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);

        var (brand, products) = await _catalog.GetBrandAsync(
            slug, order, ParsePage(page), cancellationToken);

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
            RouteName = "storefront-brand",
            RouteSlug = brand.Slug,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        string? sort,
        int? page,
        CancellationToken cancellationToken)
    {
        var order = ParseSort(sort);
        var products = await _catalog.SearchAsync(q, order, ParsePage(page), cancellationToken);

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
            RouteName = "storefront-search",
        });
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
