using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Inventory;

/// <summary>
/// Recomputes every stock balance from the ledger.
///
/// This is the operation that makes rule 1 worth having. Because balances are a
/// projection and the ledger is append-only, a balance that disagrees with the
/// ledger is always the balance being wrong - and always fixable. Without it,
/// a divergence would be permanent and unexplainable, and somebody would end up
/// editing a quantity by hand.
///
/// Expected to find nothing. Running it should be boring.
/// </summary>
public class StockBalanceRebuildService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;

    public StockBalanceRebuildService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<OperationResult<BalanceRebuildSummary>> RebuildAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.ExecuteInTransactionAsync(RebuildCoreAsync, cancellationToken);
    }

    private async Task<OperationResult<BalanceRebuildSummary>> RebuildCoreAsync(
        CancellationToken cancellationToken)
    {
        var summary = new BalanceRebuildSummary();

        // The truth: what the ledger says about every (product, warehouse,
        // batch) that has ever moved.
        var fromLedger = await _db.StockLedger
            .AsNoTracking()
            .GroupBy(e => new { e.ProductVariantId, e.WarehouseId, e.StockBatchId })
            .Select(g => new
            {
                g.Key.ProductVariantId,
                g.Key.WarehouseId,
                g.Key.StockBatchId,
                Quantity = g.Sum(e => e.QuantityChange),
                LastMovement = g.Max(e => e.OccurredAtUtc),
            })
            .ToListAsync(cancellationToken);

        var existing = await _db.StockBalances.ToListAsync(cancellationToken);

        var byKey = existing.ToDictionary(
            b => (b.ProductVariantId, b.WarehouseId, b.StockBatchId));

        var skus = await SkuLookupAsync(fromLedger.Select(l => l.ProductVariantId), cancellationToken);

        var seen = new HashSet<(long, long, long)>();

        foreach (var truth in fromLedger)
        {
            var key = (truth.ProductVariantId, truth.WarehouseId, truth.StockBatchId);
            seen.Add(key);
            summary.Checked++;

            if (byKey.TryGetValue(key, out var balance))
            {
                if (balance.QuantityOnHand == truth.Quantity)
                {
                    continue;
                }

                summary.Discrepancies.Add(new BalanceDiscrepancy
                {
                    ProductVariantId = truth.ProductVariantId,
                    Sku = skus.GetValueOrDefault(truth.ProductVariantId, string.Empty),
                    StockBatchId = truth.StockBatchId,
                    Was = balance.QuantityOnHand,
                    Now = truth.Quantity,
                });

                // Only the on-hand figure is rebuilt. Reservations are not
                // derivable from the ledger - they are commitments against
                // orders that have not shipped - and wiping them here would
                // quietly release stock somebody has already been promised.
                balance.QuantityOnHand = truth.Quantity;
                balance.LastMovementAtUtc = truth.LastMovement;

                summary.Corrected++;
            }
            else
            {
                _db.StockBalances.Add(new StockBalance
                {
                    ProductVariantId = truth.ProductVariantId,
                    WarehouseId = truth.WarehouseId,
                    StockBatchId = truth.StockBatchId,
                    QuantityOnHand = truth.Quantity,
                    QuantityReserved = 0m,
                    LastMovementAtUtc = truth.LastMovement,
                });

                summary.Discrepancies.Add(new BalanceDiscrepancy
                {
                    ProductVariantId = truth.ProductVariantId,
                    Sku = skus.GetValueOrDefault(truth.ProductVariantId, string.Empty),
                    StockBatchId = truth.StockBatchId,
                    Was = 0m,
                    Now = truth.Quantity,
                });

                summary.Created++;
            }
        }

        // A balance with no ledger behind it claims stock that never arrived.
        // Zeroed rather than deleted, so any reservation on it stays visible
        // instead of vanishing along with the row.
        foreach (var orphan in existing.Where(b =>
                     !seen.Contains((b.ProductVariantId, b.WarehouseId, b.StockBatchId))
                     && b.QuantityOnHand != 0m))
        {
            summary.Discrepancies.Add(new BalanceDiscrepancy
            {
                ProductVariantId = orphan.ProductVariantId,
                Sku = skus.GetValueOrDefault(orphan.ProductVariantId, string.Empty),
                StockBatchId = orphan.StockBatchId,
                Was = orphan.QuantityOnHand,
                Now = 0m,
            });

            orphan.QuantityOnHand = 0m;
            summary.Zeroed++;
        }

        // Logged whatever the outcome. A rebuild that silently corrected a
        // figure is exactly the event somebody needs to find later, and a
        // rebuild that found nothing is worth being able to prove.
        await _audit.LogAsync(
            AuditActions.StockBalanceRebuilt,
            nameof(StockBalance),
            null,
            summary.Describe(),
            new
            {
                summary.Checked,
                summary.Corrected,
                summary.Created,
                summary.Zeroed,
                RunAtUtc = _clock.UtcNow,

                // Capped: a rebuild that corrects thousands of rows is a
                // catastrophe worth investigating from the ledger itself, not
                // one worth writing into a single audit row.
                Discrepancies = summary.Discrepancies.Take(50),
            },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<BalanceRebuildSummary>.Success(summary);
    }

    private async Task<Dictionary<long, string>> SkuLookupAsync(
        IEnumerable<long> variantIds,
        CancellationToken cancellationToken)
    {
        var ids = variantIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku })
            .ToDictionaryAsync(v => v.Id, v => v.Sku, cancellationToken);
    }
}
