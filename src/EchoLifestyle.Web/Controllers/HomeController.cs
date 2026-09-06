using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    /// <summary>
    /// The storefront will live here from Phase 5. Until then the root simply
    /// forwards to the back office.
    /// </summary>
    public IActionResult Index() =>
        RedirectToAction("Index", "Dashboard", new { area = "BackOffice" });
}
