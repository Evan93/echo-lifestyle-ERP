using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Inventory;

namespace EchoLifestyle.Domain.Purchasing;

/// <summary>
/// Stock arriving from a supplier.
///
/// <see cref="PurchaseOrderId"/> being nullable is what makes Quick Purchase
/// possible: a receipt with no order behind it is one screen and a Post button.
/// The formal order-and-approval route produces the same document by a longer
/// path, and both post identical ledger entries - there is one way stock enters
/// this system.
/// </summary>
public class GoodsReceipt : AuditableEntity
{
    /// <summary>Unique, generated at creation, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public long SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    public long WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    /// <summary>Null for a Quick Purchase - stock that arrived without an order.</summary>
    public long? PurchaseOrderId { get; set; }

    /// <summary>Business date in Asia/Dhaka. What stock and cost reports group by.</summary>
    public DateOnly ReceiptDate { get; set; }

    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;

    /// <summary>The supplier's invoice currency. BDT unless they are an import source.</summary>
    public string CurrencyCode { get; set; } = "BDT";

    /// <summary>
    /// Units of BDT per unit of <see cref="CurrencyCode"/>, frozen on the
    /// document. Recording it here rather than looking it up at report time is
    /// what stops last year's costs moving when the rate does.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    public string? SupplierInvoiceNumber { get; set; }

    public DateOnly? SupplierInvoiceDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Sum of the lines, in BDT.</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Freight, duty, clearing and the rest, in BDT.</summary>
    public decimal ChargeTotal { get; set; }

    public decimal GrandTotal { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public long? PostedByUserId { get; set; }

    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();

    public ICollection<PurchaseCharge> Charges { get; set; } = new List<PurchaseCharge>();
}

public enum GoodsReceiptStatus
{
    /// <summary>Being typed. Nothing has moved.</summary>
    Draft = 1,

    /// <summary>Stock has moved and the ledger says so. Immutable from here.</summary>
    Posted = 2,

    /// <summary>Reversed after posting, or abandoned before it.</summary>
    Cancelled = 3,
}

public class GoodsReceiptLine : BaseEntity
{
    public long GoodsReceiptId { get; set; }

    public GoodsReceipt? GoodsReceipt { get; set; }

    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>In the supplier's currency, before charges.</summary>
    public decimal UnitCost { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>Quantity x UnitCost - Discount, in the supplier's currency.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>The same figure converted at the document's exchange rate.</summary>
    public decimal LineTotalBase { get; set; }

    /// <summary>This line's share of the document's charges, in BDT.</summary>
    public decimal ApportionedCharge { get; set; }

    /// <summary>
    /// (LineTotalBase + ApportionedCharge) / Quantity. Computed once at posting
    /// and frozen: this is the number the ledger records and margin is measured
    /// against.
    /// </summary>
    public decimal LandedUnitCost { get; set; }

    public string? BatchNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public DateOnly? ManufactureDate { get; set; }

    /// <summary>Set at posting, when the batch is created.</summary>
    public long? StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }
}

/// <summary>
/// Freight, duty, clearing - anything paid to get the shipment here that is not
/// the goods themselves.
///
/// Recorded in BDT even on an import: duty and clearing are paid locally, and
/// international freight billed in another currency is converted by whoever
/// types it. One currency on this table keeps apportionment arithmetic honest
/// without a second exchange rate nobody would maintain.
/// </summary>
public class PurchaseCharge : BaseEntity
{
    public long GoodsReceiptId { get; set; }

    public GoodsReceipt? GoodsReceipt { get; set; }

    public PurchaseChargeType ChargeType { get; set; }

    public string? Description { get; set; }

    /// <summary>In BDT.</summary>
    public decimal Amount { get; set; }

    public ChargeApportionMethod ApportionMethod { get; set; } = ChargeApportionMethod.ByValue;
}

public enum PurchaseChargeType
{
    Freight = 1,
    CustomsDuty = 2,
    Clearing = 3,
    Insurance = 4,
    Other = 5,
}

public enum ChargeApportionMethod
{
    /// <summary>
    /// Split in proportion to line value. The right default: duty and insurance
    /// both scale with what the goods are worth.
    /// </summary>
    ByValue = 1,

    /// <summary>Split per unit. Right for a per-carton handling fee.</summary>
    ByQuantity = 2,

    /// <summary>
    /// Split by weight. Right for freight, and only usable when the variants
    /// carry weights - it falls back to quantity when they do not.
    /// </summary>
    ByWeight = 3,
}
