using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Web.Areas.Storefront.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// "Where is my order?"
///
/// One page that is both the form and the answer, because the answer is the
/// thing somebody came for and a redirect to a second URL would only put the
/// order number in their browser history and in ours.
///
/// The lookup is a GET, so a customer can bookmark it and re-check the same
/// order tomorrow without retyping. That does put the order number and phone in
/// the query string - a real cost, since URLs land in server logs and get
/// pasted into Messenger - but the alternative, a POST, makes the page
/// unbookmarkable and unrefreshable, which is exactly what people do with a
/// tracking page. The information at stake is a delivery status, and the page
/// carries noindex so it is never crawled.
/// </summary>
[EnableRateLimiting(RateLimits.Tracking)]
public class TrackController : StorefrontControllerBase
{
    private readonly StorefrontTrackingService _tracking;

    public TrackController(StorefrontTrackingService tracking)
    {
        _tracking = tracking;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? order,
        string? phone,
        CancellationToken cancellationToken)
    {
        // Never indexed: it is a different page for every visitor, half of them
        // are empty forms, and the other half contain somebody's address.
        ViewData["NoIndex"] = true;
        ViewData["Title"] = "Track your order";

        // Both halves, or it is not a search. The order-received page links here
        // with the order number already filled in and the phone left blank; if
        // one field counted as asking, that link would greet a customer with
        // "we could not find that order" before they had typed anything.
        var asked = !string.IsNullOrWhiteSpace(order) && !string.IsNullOrWhiteSpace(phone);

        var model = new TrackOrderViewModel
        {
            OrderNumber = order,
            Phone = phone,
            Searched = asked,
        };

        if (!asked)
        {
            return View(model);
        }

        model.Order = await _tracking.FindAsync(order, phone, cancellationToken);

        return View(model);
    }
}
