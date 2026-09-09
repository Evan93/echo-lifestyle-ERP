using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Crm;

namespace EchoLifestyle.Domain.Sales;

/// <summary>
/// What somebody has put in their basket, and nothing more.
///
/// A cart is an intention, not a claim on stock. It reserves nothing: two
/// people may hold the last jar at once, and the first whose order is confirmed
/// gets it. Reserving at add-to-cart would let anyone empty the shelf by
/// filling a basket and walking away, and cash on delivery gives them no reason
/// not to.
///
/// Kept in the database rather than in session state, because sessions die with
/// the process and a basket that empties itself on a deploy is a lost order.
/// Identified by a random token in the visitor's cookie, so there is no account
/// to create before shopping.
///
/// Prices are deliberately absent. Every render re-reads them from the price
/// list, so a basket left open for three days cannot hold yesterday's price
/// (rule 4).
/// </summary>
public class Cart : BaseEntity
{
    /// <summary>
    /// The value in the visitor's cookie. Random, not sequential: it is the
    /// only thing standing between one shopper's basket and another's.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Set once the basket becomes an order, so it stops being offered back to
    /// the browser that still holds the cookie.
    /// </summary>
    public long? ConvertedToSalesOrderId { get; set; }

    public SalesOrder? ConvertedToSalesOrder { get; set; }

    /// <summary>
    /// Filled in at checkout. Kept afterwards because an abandoned basket with
    /// a known customer is the one worth a message, when there is somebody to
    /// send it.
    /// </summary>
    public long? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Moved on every change. What an eventual cleanup job sweeps by.</summary>
    public DateTime LastTouchedAtUtc { get; set; }

    public ICollection<CartLine> Lines { get; set; } = new List<CartLine>();

    public bool IsConverted => ConvertedToSalesOrderId is not null;

    /// <summary>A basket cannot hold more kinds of thing than this.</summary>
    public const int MaxLines = 40;

    /// <summary>Nor more of any one thing. Past this it is a wholesale enquiry.</summary>
    public const decimal MaxQuantityPerLine = 20m;
}

public class CartLine : BaseEntity
{
    public long CartId { get; set; }

    public Cart? Cart { get; set; }

    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public decimal Quantity { get; set; }

    public DateTime AddedAtUtc { get; set; }
}
