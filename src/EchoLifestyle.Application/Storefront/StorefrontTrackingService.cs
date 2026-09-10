using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Text;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// "Where is my order?", answered without an account.
///
/// Accounts do not exist at launch - checkout is guest-only, by phone number -
/// so the order number plus the phone it was placed with is the credential. It
/// is a weak one and is treated as such:
///
/// <list type="bullet">
/// <item>Both halves are required. Order numbers run in sequence, so a phone
/// number alone, or an order number alone, would let anybody walk the list.</item>
/// <item>Every failure returns the same nothing. Distinguishing "no such order"
/// from "wrong phone" would turn this into a tool for discovering which order
/// numbers exist, and roughly how many orders the business takes.</item>
/// <item>The caller rate-limits it. Without a ceiling, "both halves" is only
/// eighty million guesses per order number, which is a weekend's work.</item>
/// <item>What comes back is a deliberate subset. Somebody who does guess their
/// way in gets a delivery status, not a customer file.</item>
/// </list>
///
/// The remaining exposure is honest and worth stating: anyone holding a
/// customer's order slip and their phone number can see that order. That is the
/// same as every other guest-checkout shop, and it is the price of not making
/// people create an account to ask a one-word question.
/// </summary>
public class StorefrontTrackingService
{
    private readonly IApplicationDbContext _db;

    public StorefrontTrackingService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The order, or null. Null means "that pair does not match an order" and
    /// deliberately nothing more precise than that.
    /// </summary>
    public async Task<TrackedOrder?> FindAsync(
        string? orderNumber,
        string? phone,
        CancellationToken cancellationToken = default)
    {
        var number = orderNumber?.Trim().ToUpperInvariant();

        // Normalised before comparing, because the number typed into this box
        // will not be typed the way it was at checkout: +880, spaces, dashes.
        // Comparing raw strings would fail for the very customers who are most
        // likely to be looking - the ones who saved their number with a +880.
        var normalised = BangladeshPhone.Normalise(phone);

        if (string.IsNullOrWhiteSpace(number) || normalised is null)
        {
            return null;
        }

        var order = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Number == number

                        // Either the number the parcel is going to, or the one
                        // the account was placed under. A gift ordered for
                        // somebody else has two, and the person asking is
                        // usually whichever one they used.
                        && (o.RecipientPhone == normalised
                            || o.Customer!.Phone == normalised))
            .Select(o => new TrackedOrder
            {
                Number = o.Number,
                OrderDate = o.OrderDate,
                Status = o.Status,
                RecipientName = o.RecipientName,

                DeliveryAddress = o.AddressLine + ", " + o.AreaOrThana + ", " + o.DistrictName,

                CourierName = o.CourierName,
                ConsignmentNumber = o.ConsignmentNumber,

                SubTotal = o.SubTotal,
                DiscountAmount = o.DiscountAmount,
                DeliveryCharge = o.DeliveryCharge,
                GrandTotal = o.GrandTotal,
                AmountCollected = o.AmountCollected,

                DispatchedAtUtc = o.DispatchedAtUtc,
                DeliveredAtUtc = o.DeliveredAtUtc,

                Lines = o.Lines
                    .OrderBy(l => l.Id)
                    .Select(l => new TrackedOrderLine
                    {
                        // The snapshot on the line, not the product's name
                        // today. What was bought is what the page should say
                        // was bought, even if the product has been renamed.
                        ProductName = l.ProductName,
                        VariantName = l.VariantName,

                        // Only where the product is still on the site. A link
                        // to a page that would 404 is worse than no link.
                        ProductSlug = l.ProductVariant!.Product!.IsPublished
                                      && l.ProductVariant.Product.IsActive
                            ? l.ProductVariant.Product.Slug
                            : null,

                        Quantity = l.Quantity,
                        LineTotal = l.LineTotal,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return order;
    }
}
