using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Inventory.Counts;

/// <summary>
/// Counting the shelf and reconciling it with the ledger.
///
/// Three steps, deliberately separate: generate a sheet, type in what was found,
/// post the differences. The sheet freezes a snapshot of what the system
/// believed at that moment, and posting moves stock by <em>counted minus
/// snapshot</em> - never by setting the balance to the counted figure.
///
/// That distinction is the whole design. A count takes an afternoon; sales
/// happen during it. Overwriting the balance with the counted number would
/// silently put back every unit sold since the sheet was printed. Posting a
/// delta leaves those sales alone and corrects only what the count actually
/// found.
/// </summary>
public partial class StockCountService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly StockMovementWriter _movements;

    public StockCountService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditLogger audit,
        StockMovementWriter movements)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _movements = movements;
    }

    /// <summary>
    /// Generates a count sheet: every batch with stock in the warehouse, within
    /// the chosen scope, with what the system believes frozen onto each line.
    /// </summary>
    public async Task<OperationResult<long>> StartAsync(
        StartCountRequest request,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.IsActive, cancellationToken);

        if (warehouse is null)
        {
            return OperationResult<long>.Failure(
                "Choose an active warehouse.", nameof(StartCountRequest.WarehouseId));
        }

        if (!await _db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, cancellationToken))
        {
            return OperationResult<long>.Failure(
                "Choose an active branch.", nameof(StartCountRequest.BranchId));
        }

        // One open count per warehouse. Two sheets against the same shelves are
        // two sets of variances measured from the same snapshot, and posting
        // both would apply every correction twice. The database carries the same
        // rule as a filtered unique index.
        var open = await _db.StockCounts
            .AsNoTracking()
            .Where(c => c.WarehouseId == warehouse.Id && c.Status == StockCountStatus.Counting)
            .Select(c => c.Number)
            .FirstOrDefaultAsync(cancellationToken);

        if (open is not null)
        {
            return OperationResult<long>.Failure(
                $"{open} is still open for {warehouse.Name}. Finish or cancel it before starting "
                + "another - two counts of the same shelves would each correct the same difference.");
        }

        var scoped = await ResolveScopeAsync(request, cancellationToken);

        if (!scoped.Succeeded)
        {
            return OperationResult<long>.Failure(scoped.Error!, scoped.Field);
        }

        var scope = scoped.Value!;
        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var countDate = request.CountDate ?? today;

        if (countDate > today)
        {
            return OperationResult<long>.Failure(
                "A count cannot be dated in the future.", nameof(StartCountRequest.CountDate));
        }

        // Checked before opening a transaction, so an empty selection is a plain
        // refusal rather than a rollback.
        if (!await AnythingToCountAsync(warehouse.Id, scope, cancellationToken))
        {
            return OperationResult<long>.Failure(
                "There is no stock to count in that selection. Receive something first, or widen "
                + "the scope.");
        }

        var now = _clock.UtcNow;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                // Taken inside the transaction: if the execution strategy retries
                // it, the sheet must be frozen against the state the retry sees,
                // not the state of the attempt that failed.
                var snapshot = await SnapshotAsync(warehouse.Id, scope, token);

                var count = new StockCount
                {
                    Number = await NextNumberAsync(countDate, token),
                    WarehouseId = warehouse.Id,
                    BranchId = request.BranchId,
                    CountDate = countDate,
                    Scope = request.Scope,
                    ScopeId = scope.ScopeId,
                    ScopeName = scope.ScopeName,
                    Status = StockCountStatus.Counting,
                    Notes = Trim(request.Notes),
                    SnapshotAtUtc = now,
                };

                foreach (var row in snapshot)
                {
                    count.Lines.Add(new StockCountLine
                    {
                        ProductVariantId = row.ProductVariantId,
                        StockBatchId = row.StockBatchId,
                        SystemQuantity = row.QuantityOnHand,

                        // Cost is frozen with the quantity. Valuing a variance
                        // months later at whatever the batch costs then would
                        // report a loss the business never took.
                        LandedUnitCost = row.LandedUnitCost,
                        CountedQuantity = null,
                    });
                }

                _db.StockCounts.Add(count);
                await _db.SaveChangesAsync(token);

                await _audit.LogAsync(
                    AuditActions.StockCountStarted,
                    nameof(StockCount),
                    count.Id.ToString(CultureInfo.InvariantCulture),
                    $"Started {count.Number} in {warehouse.Name}: {snapshot.Count} line(s) to count.",
                    new { count.Number, count.Scope, count.ScopeName, Lines = snapshot.Count },
                    request.BranchId,
                    token);

                await _db.SaveChangesAsync(token);

                return OperationResult<long>.Success(count.Id);
            },
            cancellationToken);
    }

    /// <summary>
    /// Saves what has been counted so far. Safe to call repeatedly - a count is
    /// typed in over an afternoon, not in one go.
    /// </summary>
    public async Task<OperationResult> SaveCountsAsync(
        long id,
        IReadOnlyList<CountEntryInput> entries,
        CancellationToken cancellationToken = default)
    {
        var count = await _db.StockCounts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (count is null)
        {
            return OperationResult.Failure("That count no longer exists.");
        }

        if (count.Status != StockCountStatus.Counting)
        {
            return OperationResult.Failure(
                $"{count.Number} is {count.Status.ToString().ToLowerInvariant()} and can no longer "
                + "be edited.");
        }

        // Validated before anything is applied, so a bad figure halfway down a
        // long sheet does not leave the earlier half changed and the rest not.
        if (entries.Any(e => e.CountedQuantity is < 0m))
        {
            return OperationResult.Failure(
                "A counted quantity cannot be negative - there is no such thing as minus three on "
                + "a shelf.");
        }

        var byId = count.Lines.ToDictionary(l => l.Id);

        foreach (var entry in entries)
        {
            if (!byId.TryGetValue(entry.LineId, out var line))
            {
                // A line id that is not on this sheet is a stale form, not
                // something to guess at.
                continue;
            }

            // Null clears the line back to uncounted, which is a real state and
            // not the same as counting zero. Zero means "I looked and there was
            // nothing"; null means "nobody has been to that shelf".
            line.CountedQuantity = entry.CountedQuantity;
            line.Notes = Trim(entry.Notes);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Turns counted variances into stock movements.
    ///
    /// Only counted lines with a difference move. Uncounted lines are skipped
    /// entirely rather than treated as zero: reading a blank as "none found"
    /// would write off every batch nobody reached, which is the single most
    /// expensive default a stock count can have.
    /// </summary>
    public async Task<OperationResult<StockCountPostSummary>> PostAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var count = await _db.StockCounts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (count is null)
        {
            return OperationResult<StockCountPostSummary>.Failure("That count no longer exists.");
        }

        if (count.Status != StockCountStatus.Counting)
        {
            return OperationResult<StockCountPostSummary>.Failure(
                $"{count.Number} is already {count.Status.ToString().ToLowerInvariant()}.");
        }

        var counted = count.Lines.Where(l => l.CountedQuantity is not null).ToList();

        if (counted.Count == 0)
        {
            return OperationResult<StockCountPostSummary>.Failure(
                $"Nothing on {count.Number} has been counted yet, so there is nothing to post.");
        }

        var moving = counted.Where(l => l.Variance is not null && l.Variance != 0m).ToList();
        var skipped = count.Lines.Count - counted.Count;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var now = _clock.UtcNow;

                foreach (var line in moving)
                {
                    var variance = line.Variance!.Value;

                    // The delta, applied on top of whatever the balance is now.
                    // Setting the balance to the counted figure instead would
                    // undo every sale made while the count was in progress.
                    await _movements.AppendAsync(
                        new StockMovement
                        {
                            ProductVariantId = line.ProductVariantId,
                            WarehouseId = count.WarehouseId,
                            StockBatchId = line.StockBatchId,
                            MovementType = variance > 0m
                                ? StockMovementType.AdjustmentIn
                                : StockMovementType.AdjustmentOut,
                            QuantityChange = variance,
                            UnitCost = line.LandedUnitCost,
                            BranchId = count.BranchId,
                            DocumentType = StockDocumentType.StockCount,
                            DocumentId = count.Id,
                            DocumentNumber = count.Number,
                            OccurredAtUtc = now,
                            BusinessDate = count.CountDate,
                            Notes = line.Notes
                                    ?? $"Counted {line.CountedQuantity:N0}, system said "
                                       + $"{line.SystemQuantity:N0}.",
                        },
                        token);
                }

                count.Status = StockCountStatus.Posted;
                count.PostedAtUtc = now;
                count.PostedByUserId = _currentUser.UserId;

                var summary = new StockCountPostSummary
                {
                    Number = count.Number,
                    LinesPosted = moving.Count,
                    LinesSkipped = skipped,
                    NetUnits = moving.Sum(l => l.Variance!.Value),
                    NetValue = moving.Sum(l => l.VarianceValue),
                };

                await _audit.LogAsync(
                    AuditActions.StockCountPosted,
                    nameof(StockCount),
                    count.Id.ToString(CultureInfo.InvariantCulture),
                    summary.Describe(),
                    new
                    {
                        count.Number,
                        count.Scope,
                        count.ScopeName,
                        summary.LinesPosted,
                        summary.LinesSkipped,
                        summary.NetUnits,
                        summary.NetValue,

                        // Capped: a full count of a growing catalogue would
                        // otherwise write an audit row nobody can open.
                        Variances = moving
                            .Take(100)
                            .Select(l => new
                            {
                                l.ProductVariantId,
                                l.StockBatchId,
                                l.SystemQuantity,
                                l.CountedQuantity,
                                l.LandedUnitCost,
                            }),
                    },
                    count.BranchId,
                    token);

                await _db.SaveChangesAsync(token);

                return OperationResult<StockCountPostSummary>.Success(summary);
            },
            cancellationToken);
    }

    /// <summary>Abandons an open count. Nothing has moved, so nothing is reversed.</summary>
    public async Task<OperationResult> CancelAsync(
        long id,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (count is null)
        {
            return OperationResult.Failure("That count no longer exists.");
        }

        if (count.Status == StockCountStatus.Posted)
        {
            return OperationResult.Failure(
                $"{count.Number} has already posted. Its movements are in the ledger, so a mistake "
                + "is corrected with an adjustment rather than by cancelling this.");
        }

        if (count.Status == StockCountStatus.Cancelled)
        {
            return OperationResult.Success();
        }

        count.Status = StockCountStatus.Cancelled;
        count.Notes = string.IsNullOrWhiteSpace(reason)
            ? count.Notes
            : $"{count.Notes}\nCancelled: {reason.Trim()}".TrimStart();

        await _audit.LogAsync(
            AuditActions.StockCountCancelled,
            nameof(StockCount),
            count.Id.ToString(CultureInfo.InvariantCulture),
            $"Cancelled {count.Number}. {Trim(reason)}".TrimEnd(),
            new { count.Number, Reason = Trim(reason) },
            count.BranchId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------
    // Scope and snapshot
    // -----------------------------------------------------------------------

    private async Task<OperationResult<ResolvedScope>> ResolveScopeAsync(
        StartCountRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Scope)
        {
            case StockCountScope.Everything:
                return OperationResult<ResolvedScope>.Success(new ResolvedScope());

            case StockCountScope.Brand:
            {
                var brand = await _db.Brands
                    .AsNoTracking()
                    .Where(b => b.Id == request.ScopeId)
                    .Select(b => new { b.Id, b.Name })
                    .FirstOrDefaultAsync(cancellationToken);

                if (brand is null)
                {
                    return OperationResult<ResolvedScope>.Failure(
                        "Choose a brand to count.", nameof(StartCountRequest.ScopeId));
                }

                return OperationResult<ResolvedScope>.Success(new ResolvedScope
                {
                    ScopeId = brand.Id,
                    ScopeName = brand.Name,
                    BrandId = brand.Id,
                });
            }

            case StockCountScope.Category:
            {
                var category = await _db.Categories
                    .AsNoTracking()
                    .Where(c => c.Id == request.ScopeId)
                    .Select(c => new { c.Id, c.Name, c.Path })
                    .FirstOrDefaultAsync(cancellationToken);

                if (category is null)
                {
                    return OperationResult<ResolvedScope>.Failure(
                        "Choose a category to count.", nameof(StartCountRequest.ScopeId));
                }

                // Descendants included. Somebody who says "count Skincare"
                // means the shelves, not the one category node that happens to
                // have products attached directly to it.
                return OperationResult<ResolvedScope>.Success(new ResolvedScope
                {
                    ScopeId = category.Id,
                    ScopeName = category.Name,
                    CategoryPath = category.Path,
                });
            }

            default:
                return OperationResult<ResolvedScope>.Failure(
                    "That is not a scope this system knows about.", nameof(StartCountRequest.Scope));
        }
    }

    private async Task<bool> AnythingToCountAsync(
        long warehouseId,
        ResolvedScope scope,
        CancellationToken cancellationToken) =>
        await InScope(warehouseId, scope).AnyAsync(cancellationToken);

    private async Task<List<SnapshotRow>> SnapshotAsync(
        long warehouseId,
        ResolvedScope scope,
        CancellationToken cancellationToken) =>
        await InScope(warehouseId, scope)
            .Select(b => new SnapshotRow
            {
                ProductVariantId = b.ProductVariantId,
                StockBatchId = b.StockBatchId,
                QuantityOnHand = b.QuantityOnHand,
                LandedUnitCost = b.StockBatch!.LandedUnitCost,
            })
            .ToListAsync(cancellationToken);

    private IQueryable<StockBalance> InScope(long warehouseId, ResolvedScope scope)
    {
        var query = _db.StockBalances
            .AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId && b.QuantityOnHand != 0m);

        if (scope.BrandId is not null)
        {
            query = query.Where(b => b.ProductVariant!.Product!.BrandId == scope.BrandId);
        }

        if (scope.CategoryPath is not null)
        {
            var path = scope.CategoryPath;

            query = query.Where(b => b.ProductVariant!.Product!.ProductCategories
                .Any(pc => pc.Category!.Path.StartsWith(path)));
        }

        return query;
    }

    private async Task<string> NextNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = DocumentNumber.Prefix(DocumentNumber.StockCount, date);

        var used = await _db.StockCounts
            .Where(c => c.Number.StartsWith(prefix))
            .Select(c => c.Number)
            .ToListAsync(cancellationToken);

        return DocumentNumber.Next(prefix, used);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ResolvedScope
    {
        public long? ScopeId { get; init; }

        public string? ScopeName { get; init; }

        public long? BrandId { get; init; }

        /// <summary>Materialised path, matched as a prefix so descendants come too.</summary>
        public string? CategoryPath { get; init; }
    }

    private sealed class SnapshotRow
    {
        public long ProductVariantId { get; init; }

        public long StockBatchId { get; init; }

        public decimal QuantityOnHand { get; init; }

        public decimal LandedUnitCost { get; init; }
    }
}
