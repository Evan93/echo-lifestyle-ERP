using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.Storefront.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// Name, number, address, done.
///
/// No account, no password, no email. The phone number is how a customer is
/// identified everywhere else in this system, so it does all the work here too
/// - and the page never says whether it recognised the number, because a form
/// that answers that question is a form for testing whose number it is.
/// </summary>
public class CheckoutController : StorefrontControllerBase
{
    private readonly CartService _carts;
    private readonly CheckoutService _checkout;
    private readonly EchoDbContext _db;

    public CheckoutController(CartService carts, CheckoutService checkout, EchoDbContext db)
    {
        _carts = carts;
        _checkout = checkout;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var cart = await _carts.GetAsync(CartToken, cancellationToken);

        if (!cart.CanCheckOut)
        {
            return RedirectToRoute("storefront-cart");
        }

        ViewData["Title"] = "Checkout";
        ViewData["NoIndex"] = true;

        var model = new CheckoutFormModel();
        await PopulateAsync(model, cart, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.Checkout)]
    public async Task<IActionResult> Index(
        CheckoutFormModel model,
        CancellationToken cancellationToken)
    {
        var cart = await _carts.GetAsync(CartToken, cancellationToken);

        if (!cart.CanCheckOut)
        {
            return RedirectToRoute("storefront-cart");
        }

        ViewData["Title"] = "Checkout";
        ViewData["NoIndex"] = true;

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cart, cancellationToken);
            return View(model);
        }

        var request = model.ToRequest();
        request.CartToken = CartToken ?? string.Empty;

        var placed = await _checkout.PlaceAsync(request, cancellationToken);

        if (!placed.Succeeded)
        {
            ModelState.AddModelError(placed.Field ?? string.Empty, placed.Error!);
            await PopulateAsync(model, cart, cancellationToken);
            return View(model);
        }

        // The basket is spent. Clearing the cookie stops a refresh or a shared
        // link showing somebody else's confirmation on this browser.
        ClearCartToken();

        TempData["PlacedNumber"] = placed.Value!.Number;
        TempData["PlacedName"] = placed.Value.RecipientName;
        TempData["PlacedTotal"] = placed.Value.GrandTotal.ToString("N0");
        TempData["PlacedDistrict"] = placed.Value.DistrictName;

        return RedirectToRoute("storefront-order-placed");
    }

    [HttpGet]
    public IActionResult Done()
    {
        var number = TempData["PlacedNumber"] as string;

        if (string.IsNullOrWhiteSpace(number))
        {
            return RedirectToRoute("storefront-home");
        }

        ViewData["Title"] = "Order received";
        ViewData["NoIndex"] = true;

        return View(new PlacedOrder
        {
            Number = number,
            RecipientName = TempData["PlacedName"] as string ?? string.Empty,
            DistrictName = TempData["PlacedDistrict"] as string ?? string.Empty,
            GrandTotal = decimal.TryParse(
                TempData["PlacedTotal"] as string, out var total) ? total : 0m,
        });
    }

    /// <summary>
    /// Re-quotes delivery when the district changes, so the total on screen is
    /// never a guess the browser made.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Delivery(long districtId, CancellationToken cancellationToken)
    {
        var cart = await _carts.GetAsync(CartToken, cancellationToken);
        var quote = await _checkout.QuoteDeliveryAsync(districtId, cart.Subtotal, cancellationToken);

        return Json(new
        {
            charge = quote.Charge,
            isFree = quote.IsFree,
            label = quote.Describe(),
            total = cart.Subtotal + quote.Charge,
        });
    }

    private async Task PopulateAsync(
        CheckoutFormModel model,
        ShopCart cart,
        CancellationToken cancellationToken)
    {
        model.Cart = cart;

        model.Districts = await _db.Districts
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Division!.DisplayOrder)
            .ThenBy(d => d.Name)
            .Select(d => new DistrictOption
            {
                Id = d.Id,
                Name = d.Name,
                FormerName = d.FormerName,
                DivisionName = d.Division!.Name,
            })
            .ToListAsync(cancellationToken);

        var districtId = model.DistrictId > 0
            ? model.DistrictId
            : model.Districts.FirstOrDefault(d => d.Name == "Dhaka")?.Id ?? 0;

        model.Delivery = await _checkout.QuoteDeliveryAsync(
            districtId, cart.Subtotal, cancellationToken);
    }
}
