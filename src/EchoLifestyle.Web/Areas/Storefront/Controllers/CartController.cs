using EchoLifestyle.Application.Storefront;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// The basket.
///
/// Every write is a POST with an anti-forgery token, so a link in an email
/// cannot add forty jars to somebody's basket, and every one redirects
/// afterwards - a refresh on the cart page should never re-add anything.
/// </summary>
public class CartController : StorefrontControllerBase
{
    private readonly CartService _carts;

    public CartController(CartService carts)
    {
        _carts = carts;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Your basket";
        ViewData["NoIndex"] = true;

        return View(await _carts.GetAsync(CartToken, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.Cart)]
    public async Task<IActionResult> Add(
        long productVariantId,
        decimal quantity,
        string? returnSlug,
        CancellationToken cancellationToken)
    {
        var result = await _carts.AddAsync(
            CartToken, productVariantId, quantity <= 0m ? 1m : quantity, cancellationToken);

        if (!result.Succeeded)
        {
            TempData["CartMessage"] = result.Error;

            return string.IsNullOrWhiteSpace(returnSlug)
                ? RedirectToRoute("storefront-home")
                : RedirectToRoute("storefront-product", new { slug = returnSlug });
        }

        WriteCartToken(result.Value!);

        TempData["CartMessage"] = "Added to your basket.";

        return RedirectToRoute("storefront-cart");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.Cart)]
    public async Task<IActionResult> Update(
        long cartLineId,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        await _carts.SetQuantityAsync(CartToken, cartLineId, quantity, cancellationToken);

        return RedirectToRoute("storefront-cart");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.Cart)]
    public async Task<IActionResult> Remove(long cartLineId, CancellationToken cancellationToken)
    {
        await _carts.RemoveAsync(CartToken, cartLineId, cancellationToken);

        TempData["CartMessage"] = "Removed.";

        return RedirectToRoute("storefront-cart");
    }
}
