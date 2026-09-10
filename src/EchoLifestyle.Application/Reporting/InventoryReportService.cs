using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Reporting;

/// <summary>
/// What is on the shelves, what it cost, and what is about to stop being worth
/// anything.
///
/// The third of those is why this report exists in the shape it does. Skincare
/// and cosmetics expire, and expired stock is not slow-moving stock - it is
/// money already spent that turns into rubbish on a known date. The window for
/// discounting it closes quietly, and nothing else in the system shouts.
/// </summary>
public class InventoryReportService
{
    /// <summary>
    /// At or below this, a line is worth looking at. Deliberately a constant
    /// rather than a per-product reorder level: with two dozen SKUs, a real
    /// reorder point per line is a maintenance job nobody will keep up, and a
    /// wrong one is worse than a blunt one. Per-product levels belong with the
    /// purchasing planning in a later phase.
    /// </summary>
    public const decimal LowStockThreshold = 5m;

    /// <summary>Far enough ahead to still be able to sell it, or send it back.</summary>
    public const int ExpiryHorizonDays = 90;

    private readonly IApplicationDbContext _db;
    private readonly StockQueryService _stock;

    public InventoryReportService(IApplicationDbContext db, StockQueryService stock)
    {
        _db = db;
        _stock = stock;
    }

    public async Task<StockReport> GetAsync(CancellationToken cancellationToken = default)
    {

        // Valued at what it cost, from the batch each unit actually sits in,
        // never at what it sells for. Valuing stock at retail books a profit
        // that has not been earned and will not be earned on anything that
        // expires, breaks or is written off.
        var rows = await _db.StockBalances
            .AsNoTracking()
            .Where(b => b.QuantityOnHand != 0m)
            .GroupBy(b => new
            {
                b.ProductVariantId,
                b.ProductVariant!.Sku,
                b.ProductVariant.VariantName,
                ProductName = b.ProductVariant.Product!.Name,
                BrandName = b.ProductVariant.Product.Brand!.Name,
            })
            .Select(g => new StockValueRow
            {
                Sku = g.Key.Sku,
                VariantName = g.Key.VariantName,
                ProductName = g.Key.ProductName,
                BrandName = g.Key.BrandName,
                QuantityOnHand = g.Sum(b => b.QuantityOnHand),
                QuantityReserved = g.Sum(b => b.QuantityReserved),
                Value = g.Sum(b => b.QuantityOnHand * b.StockBatch!.LandedUnitCost),
            })
            .OrderByDescending(r => r.Value)
            .ToListAsync(cancellationToken);

        // Availability, not on-hand (rule 18). Stock already promised to
        // somebody else is not stock you can sell, so a line with ten on hand
        // and ten reserved belongs on this list.
        var low = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && v.Product!.IsActive)
            .Select(v => new LowStockRow
            {
                ProductId = v.ProductId,
                ProductName = v.Product!.Name,
                VariantName = v.VariantName,
                Sku = v.Sku,
                IsPublished = v.Product.IsPublished,
                Available = _db.StockBalances
                    .Where(b => b.ProductVariantId == v.Id)
                    .Sum(b => (decimal?)(b.QuantityOnHand - b.QuantityReserved)) ?? 0m,
            })
            .Where(r => r.Available <= LowStockThreshold)

            // Published first, then emptiest. A published product at zero is
            // worse than an unpublished one: somebody is being shown a thing
            // that cannot be bought.
            .OrderByDescending(r => r.IsPublished)
            .ThenBy(r => r.Available)
            .ThenBy(r => r.ProductName)
            .ToListAsync(cancellationToken);

        // Asked of the inventory module rather than queried again here.
        // Inventory already has a Near-expiry screen, and a second query would
        // be a second definition of "expiring" - two screens able to disagree
        // about the same shelf. One question, one answer.
        var expiring = await _stock.ListExpiringAsync(
            ExpiryHorizonDays, warehouseId: null, cancellationToken);

        return new StockReport
        {
            Rows = rows,
            LowStock = low,
            Expiring = expiring,
        };
    }
}
