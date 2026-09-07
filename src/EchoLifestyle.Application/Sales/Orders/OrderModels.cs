using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Sales;

namespace EchoLifestyle.Application.Sales.Orders;

/// <summary>One screen of taking an order.</summary>
public class SaveOrderRequest
{
    public long CustomerId { get; set; }

    /// <summary>
    /// Which of the customer's addresses to ship to. Its contents are copied
    /// onto the order; the id is kept only for reference.
    /// </summary>
    public long? CustomerAddressId { get; set; }

    public long BranchId { get; set; }

    public long WarehouseId { get; set; }

    public DateOnly? OrderDate { get; set; }

    public SalesChannel Channel { get; set; } = SalesChannel.Messenger;

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;

    public decimal DiscountAmount { get; set; }

    public decimal DeliveryCharge { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyList<OrderLineInput> Lines { get; set; } = [];
}

public class OrderLineInput
{
    public long ProductVariantId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// Null takes the price list's answer. A value overrides it - somebody
    /// agreed a figure in a conversation and the system should record what was
    /// actually agreed, not what it would have preferred.
    /// </summary>
    public decimal? UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Handing a parcel to a courier.</summary>
public class DispatchRequest
{
    public long OrderId { get; set; }

    public string CourierName { get; set; } = string.Empty;

    public string? ConsignmentNumber { get; set; }

    public string? Note { get; set; }
}

/// <summary>Recording what the courier actually handed back.</summary>
public class DeliveryRequest
{
    public long OrderId { get; set; }

    /// <summary>
    /// Null means the full amount. Anything less is recorded as it is - couriers
    /// remit short and late, and pretending otherwise loses the money.
    /// </summary>
    public decimal? AmountCollected { get; set; }

    public string? Note { get; set; }
}

public class SalesOrderListItem
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public long CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerPhone { get; set; } = string.Empty;

    public DateOnly OrderDate { get; set; }

    public SalesOrderStatus Status { get; set; }

    public SalesChannel Channel { get; set; }

    public string DistrictName { get; set; } = string.Empty;

    public int LineCount { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal GrandTotal { get; set; }

    public decimal AmountCollected { get; set; }

    public string? CourierName { get; set; }

    public string? ConsignmentNumber { get; set; }

    public decimal AmountOutstanding => GrandTotal - AmountCollected;

    /// <summary>Shipped but not paid for. The number a COD business watches.</summary>
    public bool IsAwaitingCash =>
        Status == SalesOrderStatus.Dispatched || (Status == SalesOrderStatus.Delivered
                                                  && AmountOutstanding > 0m);
}

public class SalesOrderDetail
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public long CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerPhone { get; set; } = string.Empty;

    public bool CustomerIsBlocked { get; set; }

    public long WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;

    public long BranchId { get; set; }

    public DateOnly OrderDate { get; set; }

    public SalesOrderStatus Status { get; set; }

    public SalesChannel Channel { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    public string RecipientPhone { get; set; } = string.Empty;

    public string DivisionName { get; set; } = string.Empty;

    public string DistrictName { get; set; } = string.Empty;

    public string AreaOrThana { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string? Landmark { get; set; }

    public string? PostCode { get; set; }

    public string? DeliveryNotes { get; set; }

    public bool IsInsideCity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal DeliveryCharge { get; set; }

    public decimal GrandTotal { get; set; }

    public decimal AmountCollected { get; set; }

    public decimal CostOfGoods { get; set; }

    public string? CourierName { get; set; }

    public string? ConsignmentNumber { get; set; }

    public DateTime? DispatchedAtUtc { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }

    public string? CancelReason { get; set; }

    public string? ReturnReason { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyList<SalesOrderLineDetail> Lines { get; set; } = [];

    public IReadOnlyList<OrderStatusEntry> History { get; set; } = [];

    public decimal AmountOutstanding => GrandTotal - AmountCollected;

    /// <summary>Meaningless before dispatch, because cost is not known until then.</summary>
    public decimal Margin => SubTotal - DiscountAmount - CostOfGoods;

    /// <summary>The delivery address on one line, as a courier reads it.</summary>
    public string DeliveryOneLine
    {
        get
        {
            var parts = new List<string> { AddressLine };

            if (!string.IsNullOrWhiteSpace(Landmark))
            {
                parts.Add($"({Landmark})");
            }

            parts.Add(AreaOrThana);
            parts.Add(DistrictName);

            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public string RecipientPhoneDisplay => BangladeshPhone.Format(RecipientPhone);
}

public class SalesOrderLineDetail
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    public decimal CostOfGoods { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// The batches promised to this line while it waits, so somebody packing
    /// knows which jar to take off the shelf.
    /// </summary>
    public IReadOnlyList<ReservedBatch> Reserved { get; set; } = [];

    public decimal Margin => LineTotal - CostOfGoods;
}

public class ReservedBatch
{
    public string BatchNumber { get; set; } = string.Empty;

    public bool IsAutoGenerated { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public decimal Quantity { get; set; }
}

public class OrderStatusEntry
{
    public SalesOrderStatus FromStatus { get; set; }

    public SalesOrderStatus ToStatus { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// A product being added to an order: what it costs the customer, and how many
/// can actually be promised.
/// </summary>
public class SellableItem
{
    public long ProductVariantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public decimal? UnitPrice { get; set; }

    /// <summary>
    /// On hand minus what other orders are already holding. The only stock
    /// figure a salesperson should ever be shown.
    /// </summary>
    public decimal Available { get; set; }

    public string Display => VariantName == "Standard"
        ? ProductName
        : $"{ProductName} - {VariantName}";
}
