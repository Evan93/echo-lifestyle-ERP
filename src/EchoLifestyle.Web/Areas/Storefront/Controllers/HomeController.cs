using EchoLifestyle.Application.Storefront;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

public class HomeController : StorefrontControllerBase
{
    private readonly StorefrontCatalogService _catalog;
    private readonly StorefrontBannerService _banners;

    public HomeController(StorefrontCatalogService catalog, StorefrontBannerService banners)
    {
        _catalog = catalog;
        _banners = banners;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Composed here rather than folded into GetHomeAsync. A banner is not a
        // product and the rule for showing one has nothing to do with stock,
        // publication or price, so the catalogue reader stays the single place
        // where "what a customer may see" can be read in full.
        ViewData["Banners"] = await _banners.GetCurrentAsync(cancellationToken);

        return View(await _catalog.GetHomeAsync(cancellationToken));
    }
}
