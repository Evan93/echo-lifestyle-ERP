using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Inventory;

namespace EchoLifestyle.Domain.Sales;

/// <summary>
/// An order, from the message that started it to the money that came back.
///
/// The delivery address is copied onto this document rather than pointed at.
/// A customer who moves house next month must not silently rewrite where last
/// month's parcel went - and when a courier disputes a delivery, the only useful
/// answer is the address as it was printed on the label.
///
/// Cash on delivery shapes the whole lifecycle. Money arrives days after the
/// goods leave, a meaningful share of parcels come back refused, and the gap
/// between "dispatched" and "paid" is where the business actually lives.
/// </summary>
public class SalesOrder : AuditableEntity
{
    /// <summary>SO-yyMM-0001. Quoted back by customers and printed on the label.</summary>
    public string Number { get; set; } = string.Empty;

    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>Where the stock is committed from and shipped out of.</summary>
    public long WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    /// <summary>Business date in Asia/Dhaka. What sales reports group by.</summary>
    public DateOnly OrderDate { get; set; }

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;

    /// <summary>Which conversation this order came out of.</summary>
    public SalesChannel Channel { get; set; } = SalesChannel.Messenger;

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;

    // -----------------------------------------------------------------------
    // Delivery - a snapshot, deliberately not a foreign key
    // -----------------------------------------------------------------------

    /// <summary>
    /// The address this was copied from, for reference only. Nullable and never
    /// read for the delivery itself: the snapshot below is what shipped.
    /// </summary>
    public long? CustomerAddressId { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    public string RecipientPhone { get; set; } = string.Empty;

    public string DivisionName { get; set; } = string.Empty;

    public string DistrictName { get; set; } = string.Empty;

    public string AreaOrThana { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string? Landmark { get; set; }

    public string? PostCode { get; set; }

    public string? DeliveryNotes { get; set; }

    /// <summary>Copied from the district. Drives the delivery charge and nothing else.</summary>
    public bool IsInsideCity { get; set; }

    // -----------------------------------------------------------------------
    // Money
    // -----------------------------------------------------------------------

    /// <summary>Sum of the lines, before any order-level discount.</summary>
    public decimal SubTotal { get; set; }

    /// <summary>A discount on the whole order, on top of anything given per line.</summary>
    public decimal DiscountAmount { get; set; }

    public decimal DeliveryCharge { get; set; }

    /// <summary>SubTotal - Discount + DeliveryCharge. What the courier collects.</summary>
    public decimal GrandTotal { get; set; }

    /// <summary>
    /// What actually came back, which is not always what was asked for.
    ///
    /// Couriers remit late, short, and net of their own fee. Recording the two
    /// figures separately is the only way to answer "how much is still owed to
    /// us" - a single paid flag cannot.
    /// </summary>
    public decimal AmountCollected { get; set; }

    public DateTime? CollectedAtUtc { get; set; }

    /// <summary>
    /// Cost of the goods that actually shipped, summed from the batches issued.
    /// Zero until dispatch, because which batch leaves is decided then.
    /// </summary>
    public decimal CostOfGoods { get; set; }

    // -----------------------------------------------------------------------
    // Courier
    // -----------------------------------------------------------------------

    public string? CourierName { get; set; }

    /// <summary>The courier's own tracking number. What a customer is given to chase.</summary>
    public string? ConsignmentNumber { get; set; }

    public DateTime? DispatchedAtUtc { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }

    // -----------------------------------------------------------------------
    // Endings that are not delivery
    // -----------------------------------------------------------------------

    public string? CancelReason { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public long? CancelledByUserId { get; set; }

    /// <summary>Why the parcel came back. The most-read field in a COD business.</summary>
    public string? ReturnReason { get; set; }

    public DateTime? ReturnedAtUtc { get; set; }

    public string? Notes { get; set; }

    public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();

    public ICollection<StockReservation> Reservations { get; set; } = new List<StockReservation>();

    public ICollection<SalesOrderStatusChange> StatusHistory { get; set; } =
        new List<SalesOrderStatusChange>();

    /// <summary>Still owed by the courier or the customer.</summary>
    public decimal AmountOutstanding => GrandTotal - AmountCollected;

    /// <summary>
    /// Stock has been promised but has not left. Only these can be cancelled
    /// outright - once a parcel is with a courier, the ending is delivered or
    /// returned, never cancelled.
    /// </summary>
    public static bool HoldsReservations(SalesOrderStatus status) =>
        status is SalesOrderStatus.Confirmed or SalesOrderStatus.Packed;

    /// <summary>Whether the order can still be edited.</summary>
    public static bool IsEditable(SalesOrderStatus status) =>
        status is SalesOrderStatus.Draft;

    public static bool IsFinished(SalesOrderStatus status) =>
        status is SalesOrderStatus.Delivered
            or SalesOrderStatus.Cancelled
            or SalesOrderStatus.Returned;
}

public enum SalesOrderStatus
{
    /// <summary>Being typed. No stock committed, nothing promised.</summary>
    Draft = 1,

    /// <summary>
    /// The customer has said yes. Stock is reserved - still on the shelf, but
    /// no longer sellable to anybody else.
    /// </summary>
    Confirmed = 2,

    /// <summary>In a box, waiting for the courier. Still reserved, still here.</summary>
    Packed = 3,

    /// <summary>Handed to the courier. Stock has left and the ledger says so.</summary>
    Dispatched = 4,

    /// <summary>Delivered and, usually, paid for.</summary>
    Delivered = 5,

    /// <summary>Called off before it shipped. Reservations released, nothing moved.</summary>
    Cancelled = 6,

    /// <summary>
    /// Came back. The customer refused it, was unreachable, or changed their
    /// mind at the door - the ordinary tax of selling cash on delivery. Stock
    /// returns to the shelf.
    /// </summary>
    Returned = 7,
}

public enum SalesChannel
{
    Messenger = 1,
    Facebook = 2,
    Instagram = 3,
    WhatsApp = 4,
    Phone = 5,
    Website = 6,
    WalkIn = 7,
    Other = 99,
}

public enum PaymentMethod
{
    /// <summary>The default, and for now very nearly the only one.</summary>
    CashOnDelivery = 1,

    /// <summary>Paid before dispatch, by any means. The detail comes with payments.</summary>
    PaidInAdvance = 2,

    Bkash = 3,
    Nagad = 4,
    Card = 5,
    BankTransfer = 6,
}

/// <summary>
/// One product on an order.
///
/// The product's name and SKU are copied here. A product renamed or rebranded
/// next season must not change what a past invoice says was sold.
/// </summary>
public class SalesOrderLine : BaseEntity
{
    public long SalesOrderId { get; set; }

    public SalesOrder? SalesOrder { get; set; }

    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    /// <summary>Snapshot. What was sold, as it was called at the time.</summary>
    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    /// <summary>Snapshot from the price list in force when the order was taken.</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>(Quantity x UnitPrice) - Discount.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>
    /// What these units cost, summed across the batches actually issued. Zero
    /// until dispatch: which batch ships is decided then, and batches do not
    /// share a cost.
    /// </summary>
    public decimal CostOfGoods { get; set; }

    public string? Notes { get; set; }

    /// <summary>Gross margin on this line. Meaningless before dispatch.</summary>
    public decimal Margin => LineTotal - CostOfGoods;
}

/// <summary>
/// Stock promised to an order but still on the shelf.
///
/// Held per batch, because that is the grain stock lives at and because
/// releasing has to put the quantity back exactly where it came from. No ledger
/// entry is written for a reservation: nothing has moved, and the ledger records
/// movements. What changes is <see cref="StockBalance.QuantityReserved"/>, which
/// is why the balance rebuild deliberately leaves that column alone - a
/// reservation is not derivable from movement history.
/// </summary>
public class StockReservation : AuditableEntity
{
    public long SalesOrderId { get; set; }

    public SalesOrder? SalesOrder { get; set; }

    public long SalesOrderLineId { get; set; }

    public SalesOrderLine? SalesOrderLine { get; set; }

    public long ProductVariantId { get; set; }

    public long WarehouseId { get; set; }

    public long StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }

    /// <summary>Always positive. Direction is not a property of a promise.</summary>
    public decimal Quantity { get; set; }
}

/// <summary>
/// Every status an order passed through, and who moved it.
///
/// Kept because "when did this ship?" and "who cancelled it?" are asked
/// constantly, and because a single status column answers neither.
/// </summary>
public class SalesOrderStatusChange : BaseEntity
{
    public long SalesOrderId { get; set; }

    public SalesOrder? SalesOrder { get; set; }

    public SalesOrderStatus FromStatus { get; set; }

    public SalesOrderStatus ToStatus { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public long? UserId { get; set; }

    public string? Note { get; set; }
}
