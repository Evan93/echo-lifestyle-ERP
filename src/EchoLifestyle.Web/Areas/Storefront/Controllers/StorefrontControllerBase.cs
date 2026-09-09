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

    // -----------------------------------------------------------------------
    // The basket cookie
    // -----------------------------------------------------------------------

    private const string CartCookie = "echo.cart";

    /// <summary>
    /// The visitor's basket token, or null.
    ///
    /// The token itself is 32 random bytes, so it needs no signing to be
    /// unguessable - guessing one is guessing a 256-bit secret. The cookie is
    /// HttpOnly so no script on the page can read it, and SameSite=Lax so
    /// another site cannot drive a basket write with the visitor's cookie
    /// attached.
    /// </summary>
    protected string? CartToken
    {
        get
        {
            var value = Request.Cookies[CartCookie];

            // Length-checked because anything else came from somewhere other
            // than this application, and a malformed token is just a miss.
            return string.IsNullOrWhiteSpace(value) || value.Length is < 32 or > 64
                ? null
                : value;
        }
    }

    protected void WriteCartToken(string token) =>
        Response.Cookies.Append(CartCookie, token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,

            // Follows the request, so a local HTTP run still works and
            // production over HTTPS still gets a Secure cookie.
            Secure = Request.IsHttps,

            // A basket that survives a fortnight is a basket somebody comes
            // back to. Beyond that it is clutter with stale prices in it.
            Expires = DateTimeOffset.UtcNow.AddDays(14),
        });

    protected void ClearCartToken() =>
        Response.Cookies.Delete(CartCookie, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
        });
}

/// <summary>
/// Rate-limit policy names.
///
/// The storefront's write endpoints are anonymous and create real rows - a
/// customer, an order, a basket. Without a ceiling, one script overnight
/// becomes ten thousand of each, and the first anybody knows of it is a
/// morning spent deleting them.
/// </summary>
public static class RateLimits
{
    public const string Cart = "storefront-cart";

    public const string Checkout = "storefront-checkout";
}
