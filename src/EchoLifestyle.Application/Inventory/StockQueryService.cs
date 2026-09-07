using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Inventory;

/// <summary>
/// Reading stock.
///
/// Every figure here comes from <see cref="StockBalance"/>, which is a
/// projection of the ledger. Nothing in this class writes, and nothing anywhere
/// computes a balance by summing the ledger on demand - that would be correct
/// and would also get slower every month.
/// </summary>
public class StockQueryService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public StockQueryService(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Stock on hand, one row per product per warehouse.
    ///
    /// Grouped in the database rather than in memory. The identifying columns
    /// are carried into the group key alongside the ids they depend on, so the
    /// result can be sorted and displayed without a second round trip for
    /// names.
    /// </summary>
    public async Task<PagedResult<StockOnHandItem>> ListOnHandAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        long? warehouseId,
        bool includeZero,
        CancellationToken cancellationToken = default)
    {
        var balances = _db.StockBalances.AsNoTracking();

        if (warehouseId is not null)
        {
            balances = balances.Where(b => b.WarehouseId == warehouseId);
        }

        var totalCount = await balances.CountAsync(cancellationToken);

        if (!includeZero)
        {
            balances = balances.Where(b => b.QuantityOnHand != 0m);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            balances = balances.Where(b =>
                EF.Functions.Like(b.ProductVariant!.Sku, $"%{term}%")
                || EF.Functions.Like(b.ProductVariant.Product!.Name, $"%{term}%")
                || EF.Functions.Like(b.ProductVariant.Product.Brand!.Name, $"%{term}%")
                || (b.ProductVariant.Barcode != null
                    && EF.Functions.Like(b.ProductVariant.Barcode, $"%{term}%")));
        }

        // Flattened first so the grouping works on plain columns; grouping
        // straight over navigations is harder for the provider to translate and
        // fails in ways that only show up against a real database.
        var flat = balances.Select(b => new
        {
            b.ProductVariantId,
            b.WarehouseId,
            Sku = b.ProductVariant!.Sku,
            ProductName = b.ProductVariant.Product!.Name,
            VariantName = b.ProductVariant.VariantName,
            BrandName = b.ProductVariant.Product.Brand!.Name,
            WarehouseName = b.Warehouse!.Name,
            b.QuantityOnHand,
            b.QuantityReserved,
            LandedUnitCost = b.StockBatch!.LandedUnitCost,
            ExpiryDate = b.StockBatch.ExpiryDate,
        });

        var grouped = flat
            .GroupBy(x => new
            {
                x.ProductVariantId,
                x.WarehouseId,
                x.Sku,
                x.ProductName,
                x.VariantName,
                x.BrandName,
                x.WarehouseName,
            })
            .Select(g => new StockOnHandItem
            {
                ProductVariantId = g.Key.ProductVariantId,
                WarehouseId = g.Key.WarehouseId,
                Sku = g.Key.Sku,
                ProductName = g.Key.ProductName,
                VariantName = g.Key.VariantName,
                BrandName = g.Key.BrandName,
                WarehouseName = g.Key.WarehouseName,
                QuantityOnHand = g.Sum(x => x.QuantityOnHand),
                QuantityReserved = g.Sum(x => x.QuantityReserved),
                BatchCount = g.Count(),
                StockValue = g.Sum(x => x.QuantityOnHand * x.LandedUnitCost),
                EarliestExpiry = g.Min(x => x.ExpiryDate),
            });

        var filteredCount = await grouped.CountAsync(cancellationToken);

        grouped = (sortColumn, sortDescending) switch
        {
            ("sku", false) => grouped.OrderBy(r => r.Sku),
            ("sku", true) => grouped.OrderByDescending(r => r.Sku),
            ("brand", false) => grouped.OrderBy(r => r.BrandName).ThenBy(r => r.ProductName),
            ("brand", true) => grouped.OrderByDescending(r => r.BrandName).ThenBy(r => r.ProductName),
            ("onHand", false) => grouped.OrderBy(r => r.QuantityOnHand),
            ("onHand", true) => grouped.OrderByDescending(r => r.QuantityOnHand),
            ("value", false) => grouped.OrderBy(r => r.StockValue),
            ("value", true) => grouped.OrderByDescending(r => r.StockValue),
            ("expiry", false) => grouped.OrderBy(r => r.EarliestExpiry ?? DateOnly.MaxValue),
            ("expiry", true) => grouped.OrderByDescending(r => r.EarliestExpiry ?? DateOnly.MinValue),
            ("name", true) => grouped.OrderByDescending(r => r.ProductName).ThenBy(r => r.VariantName),
            _ => grouped.OrderBy(r => r.ProductName).ThenBy(r => r.VariantName),
        };

        var rows = await grouped.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return new PagedResult<StockOnHandItem>(rows, totalCount, filteredCount);
    }

    /// <summary>
    /// The batches making up one product's stock, oldest expiry first.
    ///
    /// That order is not cosmetic: it is the order the stock should leave in,
    /// so anything sitting at the top for a long time is the thing about to be
    /// written off.
    /// </summary>
    public async Task<IReadOnlyList<StockBatchItem>> GetBatchesAsync(
        long productVariantId,
        long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.StockBalances
            .AsNoTracking()
            .Where(b => b.ProductVariantId == productVariantId && b.QuantityOnHand != 0m);

        if (warehouseId is not null)
        {
            query = query.Where(b => b.WarehouseId == warehouseId);
        }

        var rows = await query
            .Select(b => new StockBatchItem
            {
                StockBatchId = b.StockBatchId,
                BatchNumber = b.StockBatch!.BatchNumber,
                IsAutoGenerated = b.StockBatch.IsAutoGenerated,
                WarehouseName = b.Warehouse!.Name,
                QuantityOnHand = b.QuantityOnHand,
                QuantityReserved = b.QuantityReserved,
                LandedUnitCost = b.StockBatch.LandedUnitCost,
                ReceivedDate = b.StockBatch.ReceivedDate,
                ExpiryDate = b.StockBatch.ExpiryDate,
                SupplierName = _db.Suppliers
                    .Where(s => s.Id == b.StockBatch.SupplierId)
                    .Select(s => s.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.ReceivedDate)
            .ToList();
    }

    public async Task<PagedResult<StockMovementItem>> ListMovementsAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        long? productVariantId,
        long? warehouseId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var query = _db.StockLedger.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        if (productVariantId is not null)
        {
            query = query.Where(e => e.ProductVariantId == productVariantId);
        }

        if (warehouseId is not null)
        {
            query = query.Where(e => e.WarehouseId == warehouseId);
        }

        if (from is not null)
        {
            query = query.Where(e => e.BusinessDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(e => e.BusinessDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                EF.Functions.Like(e.DocumentNumber, $"%{term}%")
                || EF.Functions.Like(e.ProductVariant!.Sku, $"%{term}%")
                || EF.Functions.Like(e.ProductVariant.Product!.Name, $"%{term}%")
                || EF.Functions.Like(e.StockBatch!.BatchNumber, $"%{term}%"));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("date", false) => query.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id),
            ("sku", false) => query.OrderBy(e => e.ProductVariant!.Sku).ThenByDescending(e => e.Id),
            ("sku", true) => query.OrderByDescending(e => e.ProductVariant!.Sku).ThenByDescending(e => e.Id),
            ("quantity", false) => query.OrderBy(e => e.QuantityChange),
            ("quantity", true) => query.OrderByDescending(e => e.QuantityChange),
            ("document", false) => query.OrderBy(e => e.DocumentNumber),
            ("document", true) => query.OrderByDescending(e => e.DocumentNumber),

            // Newest first: the ledger is read to answer "what just happened".
            _ => query.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(e => new StockMovementItem
            {
                Id = e.Id,
                BusinessDate = e.BusinessDate,
                OccurredAtUtc = e.OccurredAtUtc,
                Sku = e.ProductVariant!.Sku,
                ProductName = e.ProductVariant.Product!.Name,
                VariantName = e.ProductVariant.VariantName,
                WarehouseName = _db.Warehouses
                    .Where(w => w.Id == e.WarehouseId)
                    .Select(w => w.Name)
                    .FirstOrDefault() ?? string.Empty,
                BatchNumber = e.StockBatch!.BatchNumber,
                BatchWasGenerated = e.StockBatch.IsAutoGenerated,
                MovementType = e.MovementType,
                QuantityChange = e.QuantityChange,
                UnitCost = e.UnitCost,
                ValueChange = e.ValueChange,
                DocumentType = e.DocumentType,
                DocumentId = e.DocumentId,
                DocumentNumber = e.DocumentNumber,
                Notes = e.Notes,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StockMovementItem>(rows, totalCount, filteredCount);
    }

    /// <summary>
    /// Batches expiring within the given window, plus anything already expired
    /// and still on the shelf.
    ///
    /// Expired stock is included rather than filtered out because it is the
    /// most urgent thing on the list - it is on a shelf, it is worth nothing,
    /// and nobody has noticed.
    /// </summary>
    public async Task<IReadOnlyList<ExpiringBatchItem>> ListExpiringAsync(
        int withinDays = 90,
        long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var horizon = today.AddDays(Math.Max(withinDays, 0));

        var query = _db.StockBalances
            .AsNoTracking()
            .Where(b => b.QuantityOnHand > 0m
                        && b.StockBatch!.ExpiryDate != null
                        && b.StockBatch.ExpiryDate <= horizon);

        if (warehouseId is not null)
        {
            query = query.Where(b => b.WarehouseId == warehouseId);
        }

        var rows = await query
            .Select(b => new ExpiringBatchItem
            {
                StockBatchId = b.StockBatchId,
                ProductVariantId = b.ProductVariantId,
                Sku = b.ProductVariant!.Sku,
                ProductName = b.ProductVariant.Product!.Name,
                VariantName = b.ProductVariant.VariantName,
                BatchNumber = b.StockBatch!.BatchNumber,
                WarehouseName = b.Warehouse!.Name,
                ExpiryDate = b.StockBatch.ExpiryDate!.Value,
                QuantityOnHand = b.QuantityOnHand,
                LandedUnitCost = b.StockBatch.LandedUnitCost,
            })
            .ToListAsync(cancellationToken);

        // Day arithmetic in memory: DateOnly subtraction does not translate,
        // and the result set here is small by definition.
        foreach (var row in rows)
        {
            row.DaysRemaining = row.ExpiryDate.DayNumber - today.DayNumber;
        }

        return rows.OrderBy(r => r.ExpiryDate).ThenBy(r => r.ProductName).ToList();
    }
}
