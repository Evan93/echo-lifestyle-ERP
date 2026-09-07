using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Inventory;

/// <summary>
/// Counting what is physically on the shelf and reconciling it with what the
/// ledger believes.
///
/// The document holds a snapshot of the system quantity taken when the sheet was
/// generated, alongside whatever was counted. Posting moves stock by the
/// difference between those two figures - never by setting the balance to the
/// counted number. Stock can and does move between counting and posting, and
/// overwriting the balance would silently undo a sale made in the meantime.
/// </summary>
public class StockCount : AuditableEntity
{
    /// <summary>CNT-yyMM-0001.</summary>
    public string Number { get; set; } = string.Empty;

    public long WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>The business date the count belongs to.</summary>
    public DateOnly CountDate { get; set; }

    public StockCountScope Scope { get; set; } = StockCountScope.Everything;

    /// <summary>The brand or category counted, when the scope narrows to one.</summary>
    public long? ScopeId { get; set; }

    /// <summary>
    /// Stored so a posted document still says what it covered, even after the
    /// brand or category it pointed at is renamed or removed.
    /// </summary>
    public string? ScopeName { get; set; }

    public StockCountStatus Status { get; set; } = StockCountStatus.Counting;

    public string? Notes { get; set; }

    /// <summary>When the sheet was generated - the moment the snapshot is true for.</summary>
    public DateTime SnapshotAtUtc { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public long? PostedByUserId { get; set; }

    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}

public enum StockCountScope
{
    /// <summary>Every batch with stock in the warehouse.</summary>
    Everything = 1,

    Brand = 2,

    Category = 3,
}

public enum StockCountStatus
{
    /// <summary>Sheet generated, numbers being entered. Nothing has moved.</summary>
    Counting = 1,

    /// <summary>Variances posted to the ledger. Immutable from here.</summary>
    Posted = 2,

    /// <summary>Abandoned. Nothing moved.</summary>
    Cancelled = 3,
}

/// <summary>
/// One batch on the count sheet.
///
/// <see cref="CountedQuantity"/> is deliberately nullable, and null does not
/// mean zero. A blank line is one nobody got to; treating it as zero would write
/// the entire batch off because somebody ran out of time - which is the single
/// most expensive default a stock count can have.
/// </summary>
public class StockCountLine : BaseEntity
{
    public long StockCountId { get; set; }

    public StockCount? StockCount { get; set; }

    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public long StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }

    /// <summary>What the balance said when the sheet was generated.</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>What was actually found. Null until somebody counts it.</summary>
    public decimal? CountedQuantity { get; set; }

    /// <summary>The batch's landed cost at snapshot time, for valuing the variance.</summary>
    public decimal LandedUnitCost { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// What posting will move, in the absence of any other movement. Null while
    /// the line is uncounted, because "no variance" and "not counted" are
    /// different states and only one of them is safe to post.
    /// </summary>
    public decimal? Variance => CountedQuantity is null ? null : CountedQuantity - SystemQuantity;

    public bool IsCounted => CountedQuantity is not null;

    /// <summary>Signed. Negative is stock the business thought it had and does not.</summary>
    public decimal VarianceValue =>
        Variance is null ? 0m : decimal.Round(Variance.Value * LandedUnitCost, 4);
}
