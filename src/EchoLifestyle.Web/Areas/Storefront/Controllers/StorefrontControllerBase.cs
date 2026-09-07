using EchoLifestyle.Application.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// Base for every public page.
///
/// Every action in this area is public, and says so.
///
/// There is deliberately no <c>[Authorize]</c> here naming the storefront
/// scheme. Paired with <c>[AllowAnonymous]</c> on the same class it would be
/// dead weight - the analyser says as much - and its only real effect would be
/// to populate <c>User</c> on pages that never read it. When an action does
/// need a signed-in customer (order tracking in 5c, accounts in Phase 7) it
/// takes its own <c>[Authorize(AuthenticationSchemes = AuthSchemes.Storefront)]</c>,
/// and this class-level <c>[AllowAnonymous]</c> has to come off and move to the
/// actions that are still public - otherwise it will silently override that
/// first authorised action and leave it open to everyone.
///
/// The separation that does the real work is elsewhere and is not weakened by
/// any of this: the back office binds to its own scheme and its own cookie, so
/// a shopper's session is not a credential there, and every back-office action
/// authorises on the server regardless.
/// </summary>
[Area("Storefront")]
[AllowAnonymous]
public abstract class StorefrontControllerBase : Controller
{
    /// <summary>
    /// Sort order from the query string, defaulting rather than failing.
    ///
    /// A hand-edited <c>?sort=cheapest</c> should show the newest products, not
    /// a 400. Nothing on a public URL is trusted enough to throw over.
    /// </summary>
    protected static ShopSort ParseSort(string? value) => value?.ToLowerInvariant() switch
    {
        "price-asc" => ShopSort.PriceLowToHigh,
        "price-desc" => ShopSort.PriceHighToLow,
        "name" => ShopSort.NameAToZ,
        _ => ShopSort.Newest,
    };

    /// <summary>
    /// Page number from the query string, floored at one.
    ///
    /// No ceiling here: an out-of-range page returns an empty grid, which is
    /// correct and cheap. Clamping to the last page would mean counting the
    /// rows twice.
    /// </summary>
    protected static int ParsePage(int? page) => page is null or < 1 ? 1 : page.Value;
}
