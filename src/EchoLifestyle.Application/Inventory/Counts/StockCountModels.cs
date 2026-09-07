using EchoLifestyle.Domain.Inventory;

namespace EchoLifestyle.Application.Inventory.Counts;

public class StartCountRequest
{
    public long WarehouseId { get; set; }

    public long BranchId { get; set; }

    public DateOnly? CountDate { get; set; }

    public StockCountScope Scope { get; set; } = StockCountScope.Everything;

    /// <summary>The brand or category to narrow to. Ignored for a full count.</summary>
    public long? ScopeId { get; set; }

    public string? Notes { get; set; }
}

/// <summary>One line of the sheet coming back from the browser.</summary>
public class CountEntryInput
{
    public long LineId { get; set; }

    /// <summary>
    /// Null means nobody has counted this yet, and it stays that way. Blank is
    /// not zero: posting skips uncounted lines and would write off an entire
    /// batch if it did not.
    /// </summary>
    public decimal? CountedQuantity { get; set; }

    public string? Notes { get; set; }
}

public class StockCountListItem
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string WarehouseName { get; set; } = string.Empty;

    public DateOnly CountDate { get; set; }

    public StockCountScope Scope { get; set; }

    public string? ScopeName { get; set; }

    public StockCountStatus Status { get; set; }

    public int LineCount { get; set; }

    public int CountedCount { get; set; }

    /// <summary>Counted lines whose figure differs from the snapshot.</summary>
    public int VarianceCount { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public string ScopeDescription => Scope == StockCountScope.Everything
        ? "Everything"
        : $"{Scope}: {ScopeName}";

    public int Progress => LineCount == 0 ? 0 : (int)Math.Round(100m * CountedCount / LineCount);
}

public class StockCountDetail
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public long WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;

    public long BranchId { get; set; }

    public DateOnly CountDate { get; set; }

    public StockCountScope Scope { get; set; }

    public string? ScopeName { get; set; }

    public StockCountStatus Status { get; set; }

    public string? Notes { get; set; }

    public DateTime SnapshotAtUtc { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public IReadOnlyList<StockCountLineDetail> Lines { get; set; } = [];

    public string ScopeDescription => Scope == StockCountScope.Everything
        ? "Everything"
        : $"{Scope}: {ScopeName}";

    public int CountedCount => Lines.Count(l => l.CountedQuantity is not null);

    public int UncountedCount => Lines.Count - CountedCount;

    public IReadOnlyList<StockCountLineDetail> Variances =>
        Lines.Where(l => l.Variance is not null && l.Variance != 0m).ToList();

    /// <summary>
    /// Signed. Negative is stock the business believed it had and does not - the
    /// number that turns into a loss when this posts.
    /// </summary>
    public decimal NetVarianceValue => Variances.Sum(l => l.VarianceValue);

    public decimal ShrinkageValue => Variances.Where(l => l.Variance < 0m).Sum(l => l.VarianceValue);

    /// <summary>
    /// Lines whose live balance has moved away from the snapshot since the sheet
    /// was generated. Not an error - stock is allowed to move mid-count - but
    /// worth showing, because posting applies the variance on top of that
    /// movement rather than overwriting it.
    /// </summary>
    public IReadOnlyList<StockCountLineDetail> Drifted =>
        Lines.Where(l => l.CurrentSystemQuantity != l.SystemQuantity).ToList();
}

public class StockCountLineDetail
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    public long StockBatchId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public string BatchNumber { get; set; } = string.Empty;

    public bool BatchWasGenerated { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    /// <summary>What the balance said when the sheet was generated.</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>What the balance says now. Equal to the snapshot unless stock moved.</summary>
    public decimal CurrentSystemQuantity { get; set; }

    public decimal? CountedQuantity { get; set; }

    public decimal LandedUnitCost { get; set; }

    public string? Notes { get; set; }

    public decimal? Variance => CountedQuantity is null ? null : CountedQuantity - SystemQuantity;

    public decimal VarianceValue =>
        Variance is null ? 0m : decimal.Round(Variance.Value * LandedUnitCost, 4);

    public bool HasDrifted => CurrentSystemQuantity != SystemQuantity;
}

/// <summary>What posting a count actually did.</summary>
public class StockCountPostSummary
{
    public string Number { get; set; } = string.Empty;

    public int LinesPosted { get; set; }

    public int LinesSkipped { get; set; }

    public decimal NetUnits { get; set; }

    public decimal NetValue { get; set; }

    public string Describe()
    {
        if (LinesPosted == 0)
        {
            return $"{Number} posted. Every counted line matched - nothing moved.";
        }

        var text = $"{Number} posted: {LinesPosted} variance(s), {NetUnits:N0} net unit(s), "
                   + $"{NetValue:N2} BDT.";

        return LinesSkipped == 0
            ? text
            : $"{text} {LinesSkipped} line(s) were left uncounted and were not touched.";
    }
}

/// <summary>A brand or category to narrow a count to.</summary>
public class CountScopeOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
