using EchoLifestyle.Application.Storefront;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

public class HomeController : StorefrontControllerBase
{
    private readonly StorefrontCatalogService _catalog;

    public HomeController(StorefrontCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await _catalog.GetHomeAsync(cancellationToken));
}
