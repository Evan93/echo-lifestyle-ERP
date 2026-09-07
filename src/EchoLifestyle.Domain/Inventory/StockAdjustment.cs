using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Inventory;

/// <summary>
/// Stock changing for a reason that is not a purchase or a sale: breakage,
/// testers, theft, a miscount found by hand.
///
/// This is the only screen in the system that can move stock without a document
/// from outside the business behind it, which is exactly why it carries a reason
/// and an approver. Everything else - receipts, sales, returns - has an invoice
/// or an order to point at.
/// </summary>
public class StockAdjustment : AuditableEntity
{
    /// <summary>ADJ-yyMM-0001. Generated at submission, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public long WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>Business date in Asia/Dhaka - the day the stock actually changed.</summary>
    public DateOnly AdjustmentDate { get; set; }

    public StockAdjustmentReason Reason { get; set; }

    public StockAdjustmentStatus Status { get; set; } = StockAdjustmentStatus.PendingApproval;

    /// <summary>
    /// Required. An adjustment with no explanation is the thing an auditor
    /// stops at, and the person who wrote it will not remember in six months.
    /// </summary>
    public string ReasonNotes { get; set; } = string.Empty;

    public long? RequestedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public long? ApprovedByUserId { get; set; }

    /// <summary>
    /// True when the person who wrote it also approved it. Legitimate with two
    /// partners and one warehouse, but recorded rather than hidden, so it can be
    /// counted later if the business ever needs separation of duties.
    /// </summary>
    public bool WasSelfApproved { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public string? DecisionNotes { get; set; }

    public ICollection<StockAdjustmentLine> Lines { get; set; } = new List<StockAdjustmentLine>();

    /// <summary>
    /// Reasons that can only take stock out. Damaged goods do not un-damage
    /// themselves, so a positive line under one of these is a data-entry error
    /// worth refusing rather than storing.
    /// </summary>
    public static bool IsOutboundOnly(StockAdjustmentReason reason) => reason switch
    {
        StockAdjustmentReason.Damaged => true,
        StockAdjustmentReason.Expired => true,
        StockAdjustmentReason.Lost => true,
        StockAdjustmentReason.Stolen => true,
        StockAdjustmentReason.TesterOrSample => true,
        StockAdjustmentReason.Promotional => true,
        _ => false,
    };

    /// <summary>Reasons that can only bring stock in.</summary>
    public static bool IsInboundOnly(StockAdjustmentReason reason) =>
        reason == StockAdjustmentReason.FoundExtra;

    /// <summary>
    /// The movement type a line of this reason posts. Write-off is kept distinct
    /// from a plain adjustment out because "we destroyed it" and "the number was
    /// wrong" are different questions, and a report that cannot separate them is
    /// not worth running.
    /// </summary>
    public static StockMovementType MovementFor(StockAdjustmentReason reason, decimal quantityChange)
    {
        if (quantityChange > 0m)
        {
            return StockMovementType.AdjustmentIn;
        }

        return reason switch
        {
            StockAdjustmentReason.Damaged => StockMovementType.WriteOff,
            StockAdjustmentReason.Expired => StockMovementType.WriteOff,
            StockAdjustmentReason.Lost => StockMovementType.WriteOff,
            StockAdjustmentReason.Stolen => StockMovementType.WriteOff,
            _ => StockMovementType.AdjustmentOut,
        };
    }
}

public enum StockAdjustmentReason
{
    /// <summary>Broken, leaked, crushed in transit.</summary>
    Damaged = 1,

    /// <summary>Past its date and taken off the shelf.</summary>
    Expired = 2,

    /// <summary>Cannot be found and nobody knows why.</summary>
    Lost = 3,

    Stolen = 4,

    /// <summary>Opened for a customer to try, or sent out as a sample.</summary>
    TesterOrSample = 5,

    /// <summary>Given away - a gift with purchase, an influencer package.</summary>
    Promotional = 6,

    /// <summary>Physically there, not in the system. The only inbound reason.</summary>
    FoundExtra = 7,

    /// <summary>
    /// The number was simply wrong. Either direction, and the one reason that
    /// says nothing about the stock itself - so it asks for the fullest note.
    /// </summary>
    Correction = 8,
}

public enum StockAdjustmentStatus
{
    /// <summary>Written and waiting. Nothing has moved.</summary>
    PendingApproval = 1,

    /// <summary>Approved, ledger written, stock moved. Immutable from here.</summary>
    Posted = 2,

    /// <summary>Turned down. Nothing moved and nothing ever will.</summary>
    Rejected = 3,
}

/// <summary>
/// One batch of one product going up or down.
///
/// The batch is not optional. Stock leaving has a cost, and that cost belongs to
/// a specific batch - taking it out "at the average" would value the loss at a
/// number nobody paid and quietly change what the remaining stock is worth.
/// </summary>
public class StockAdjustmentLine : BaseEntity
{
    public long StockAdjustmentId { get; set; }

    public StockAdjustment? StockAdjustment { get; set; }

    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public long StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }

    /// <summary>
    /// Signed, like the ledger: positive brings stock in, negative takes it out.
    /// The header's reason constrains which signs are allowed.
    /// </summary>
    public decimal QuantityChange { get; set; }

    /// <summary>The batch's landed cost, copied here when the document posts.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Signed. What this line did to the value of stock on hand.</summary>
    public decimal ValueChange { get; set; }

    public string? Notes { get; set; }
}
