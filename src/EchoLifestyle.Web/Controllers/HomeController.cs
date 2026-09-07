using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    /// <summary>
    /// The storefront owns "/" from Phase 5, through its own named route.
    ///
    /// This action exists only to catch older "/Home" links, and it redirects
    /// permanently so that one page does not end up living at two addresses.
    /// The back office moved to its own literal prefix at the same time.
    /// </summary>
    public IActionResult Index() => RedirectToRoutePermanent("storefront-home");
}
