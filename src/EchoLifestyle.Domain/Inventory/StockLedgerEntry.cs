using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Inventory;

/// <summary>
/// One movement of stock. The single source of truth for what the business
/// owns.
///
/// Nothing updates a balance directly: every screen that moves stock writes a
/// row here, and <see cref="StockBalance"/> is a projection that can be thrown
/// away and rebuilt from these rows at any time. That is what makes a
/// disagreement between the two recoverable rather than a mystery.
///
/// Append-only, enforced by the save interceptor. A correction is a reversing
/// entry, never an edit.
/// </summary>
public class StockLedgerEntry : AuditableEntity, IAppendOnly
{
    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public long WarehouseId { get; set; }

    /// <summary>
    /// Every movement is of a specific batch, including for products nobody
    /// tracks batches on - those get one generated silently at receipt. Without
    /// it, expiry and true cost would both be unanswerable.
    /// </summary>
    public long StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }

    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Signed: positive brings stock in, negative takes it out, so a balance is
    /// a plain SUM. A quantity paired with a separate direction column invites
    /// exactly one bug, and it is a silent one.
    ///
    /// The database also enforces that the sign matches the movement type.
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>
    /// The batch's landed cost at the moment of the movement, frozen here so
    /// that recosting a batch later cannot rewrite what past movements were
    /// worth.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>Signed, and stored rather than derived, for the same reason.</summary>
    public decimal ValueChange { get; set; }

    /// <summary>Which branch caused the movement. Null for opening balances.</summary>
    public long? BranchId { get; set; }

    public StockDocumentType DocumentType { get; set; }

    public long DocumentId { get; set; }

    /// <summary>
    /// Denormalised so the ledger reads without joining to five document
    /// tables. Documents are numbered once and never renumbered.
    /// </summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// The Asia/Dhaka business date the movement belongs to. Reports group by
    /// this, never by the UTC date - a sale at 2am Dhaka would otherwise land
    /// on the previous day.
    /// </summary>
    public DateOnly BusinessDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// True when the movement brings stock in. The database carries the same
    /// rule as a check constraint, so a sign error cannot be written by any
    /// route - application, script or otherwise.
    /// </summary>
    public static bool IsInbound(StockMovementType type) => type switch
    {
        StockMovementType.Receipt => true,
        StockMovementType.AdjustmentIn => true,
        StockMovementType.ReturnFromCustomer => true,
        StockMovementType.TransferIn => true,
        StockMovementType.OpeningBalance => true,
        _ => false,
    };
}

public enum StockMovementType
{
    /// <summary>Received from a supplier.</summary>
    Receipt = 1,

    /// <summary>Sold and dispatched.</summary>
    Issue = 2,

    AdjustmentIn = 3,
    AdjustmentOut = 4,
    ReturnFromCustomer = 5,
    ReturnToSupplier = 6,
    TransferOut = 7,
    TransferIn = 8,

    /// <summary>Stock that existed before the system did.</summary>
    OpeningBalance = 9,

    /// <summary>Expired, damaged or lost.</summary>
    WriteOff = 10,
}

public enum StockDocumentType
{
    GoodsReceipt = 1,
    StockAdjustment = 2,
    SalesOrder = 3,
    SalesReturn = 4,
    PurchaseReturn = 5,
    StockTransfer = 6,
    OpeningBalance = 7,
    StockCount = 8,
}
