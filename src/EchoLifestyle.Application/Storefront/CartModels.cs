namespace EchoLifestyle.Application.Storefront;

/// <summary>A basket, priced at the moment it was read.</summary>
public class ShopCart
{
    public long Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public IReadOnlyList<ShopCartLine> Lines { get; set; } = [];

    public bool IsEmpty => Lines.Count == 0;

    public int ItemCount => (int)Lines.Sum(l => l.Quantity);

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    /// <summary>
    /// Lines that can no longer be bought - unpublished, unpriced, or sold out
    /// since they were added. Shown and blocked rather than silently dropped: a
    /// basket that quietly loses an item is how somebody receives half an order
    /// and blames the courier.
    /// </summary>
    public IReadOnlyList<ShopCartLine> Problems =>
        Lines.Where(l => !l.CanBeOrdered).ToList();

    public bool CanCheckOut => Lines.Count > 0 && Problems.Count == 0;
}

public class ShopCartLine
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    public long ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductSlug { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    /// <summary>"30ml / Ruby Red", or blank when the product has one variant.</summary>
    public string? VariantName { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? ImagePath { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Read fresh on every render, never stored on the line.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal LineTotal => (UnitPrice ?? 0m) * Quantity;

    public decimal Available { get; set; }

    /// <summary>Still on sale at all - published, active, priced.</summary>
    public bool IsSellable { get; set; }

    public bool InStock => Available >= Quantity;

    public bool CanBeOrdered => IsSellable && UnitPrice is not null && InStock;

    /// <summary>What to tell the shopper when this line is blocking checkout.</summary>
    public string? Problem
    {
        get
        {
            if (!IsSellable || UnitPrice is null)
            {
                return "No longer available.";
            }

            if (Available <= 0m)
            {
                return "Out of stock.";
            }

            return Available < Quantity
                ? $"Only {Available:0.##} left."
                : null;
        }
    }
}

/// <summary>What delivery costs, and why.</summary>
public class DeliveryQuote
{
    public decimal Charge { get; set; }

    public bool IsInsideCity { get; set; }

    /// <summary>True when the order value earned free delivery.</summary>
    public bool IsFree { get; set; }

    /// <summary>The rate before the free-delivery threshold was applied.</summary>
    public decimal StandardCharge { get; set; }

    public string Describe() => IsFree
        ? "Free delivery"
        : IsInsideCity ? "Inside Dhaka city" : "Outside Dhaka city";
}

/// <summary>The checkout form, as the storefront posts it.</summary>
public class PlaceOrderRequest
{
    public string CartToken { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public long DistrictId { get; set; }

    public string AreaOrThana { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string? Landmark { get; set; }

    public string? DeliveryNotes { get; set; }
}

/// <summary>What the confirmation page shows.</summary>
public class PlacedOrder
{
    public long SalesOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public decimal GrandTotal { get; set; }

    public decimal DeliveryCharge { get; set; }

    public string DistrictName { get; set; } = string.Empty;
}
