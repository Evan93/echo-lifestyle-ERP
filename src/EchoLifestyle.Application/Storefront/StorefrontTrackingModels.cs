using EchoLifestyle.Domain.Sales;

namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// One order, as the person who placed it is allowed to see it.
///
/// A deliberate subset of <see cref="Sales.Orders.SalesOrderDetail"/>. This is
/// served on a public URL to whoever supplies a matching order number and
/// phone, so it carries no cost, no margin, no internal notes, no cancel or
/// return reason, and nothing about which branch or warehouse the stock came
/// from. Everything on it is something the customer either told us or needs.
/// </summary>
public class TrackedOrder
{
    public string Number { get; init; } = string.Empty;

    public DateOnly OrderDate { get; init; }

    public SalesOrderStatus Status { get; init; }

    public string RecipientName { get; init; } = string.Empty;

    /// <summary>Where it is going, so somebody can check they typed it correctly.</summary>
    public string DeliveryAddress { get; init; } = string.Empty;

    public string? CourierName { get; init; }

    public string? ConsignmentNumber { get; init; }

    public IReadOnlyList<TrackedOrderLine> Lines { get; init; } = [];

    public decimal SubTotal { get; init; }

    public decimal DiscountAmount { get; init; }

    public decimal DeliveryCharge { get; init; }

    public decimal GrandTotal { get; init; }

    public decimal AmountCollected { get; init; }

    public decimal AmountDue => Math.Max(0m, GrandTotal - AmountCollected);

    public DateTime? DispatchedAtUtc { get; init; }

    public DateTime? DeliveredAtUtc { get; init; }

    /// <summary>
    /// The status in the customer's language rather than the system's.
    ///
    /// "Draft" is the important one. A website order lands as a draft while
    /// somebody rings to confirm it - correct internally, and alarming to read
    /// on a page after you have just paid attention to a checkout. To the
    /// customer that state is simply "we have your order".
    /// </summary>
    public string StatusLabel => Status switch
    {
        SalesOrderStatus.Draft => "Order received",
        SalesOrderStatus.Confirmed => "Confirmed",
        SalesOrderStatus.Packed => "Packed",
        SalesOrderStatus.Dispatched => "On the way",
        SalesOrderStatus.Delivered => "Delivered",
        SalesOrderStatus.Cancelled => "Cancelled",
        SalesOrderStatus.Returned => "Returned",
        _ => "In progress",
    };

    public string StatusExplanation => Status switch
    {
        SalesOrderStatus.Draft =>
            "We have your order. Someone will call you shortly to confirm it before it ships.",
        SalesOrderStatus.Confirmed =>
            "Confirmed. We are getting it ready for the courier.",
        SalesOrderStatus.Packed =>
            "Packed and waiting for the courier to collect it.",
        SalesOrderStatus.Dispatched =>
            "Handed to the courier. Please keep your phone reachable for the delivery call.",
        SalesOrderStatus.Delivered =>
            "Delivered. Thank you.",
        SalesOrderStatus.Cancelled =>
            "This order was cancelled. Nothing is on its way and nothing is owed.",
        SalesOrderStatus.Returned =>
            "This order came back to us. If that was not what you expected, please message us.",
        _ => string.Empty,
    };

    /// <summary>
    /// Where the order sits on a four-step journey, for the progress bar.
    /// Cancelled and returned orders have left the journey rather than reached
    /// a point on it, so they get nothing.
    /// </summary>
    public int? StepsCompleted => Status switch
    {
        SalesOrderStatus.Draft => 1,
        SalesOrderStatus.Confirmed => 2,
        SalesOrderStatus.Packed => 3,
        SalesOrderStatus.Dispatched => 4,
        SalesOrderStatus.Delivered => 5,
        _ => null,
    };

    public bool IsFinished =>
        Status is SalesOrderStatus.Delivered
            or SalesOrderStatus.Cancelled
            or SalesOrderStatus.Returned;
}

public class TrackedOrderLine
{
    public string ProductName { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    /// <summary>Set when the product is still on sale, so the line can link back to it.</summary>
    public string? ProductSlug { get; init; }

    public decimal Quantity { get; init; }

    public decimal LineTotal { get; init; }

    public bool ShowVariantName =>
        !string.IsNullOrWhiteSpace(VariantName)
        && !string.Equals(VariantName, ProductName, StringComparison.OrdinalIgnoreCase);
}
