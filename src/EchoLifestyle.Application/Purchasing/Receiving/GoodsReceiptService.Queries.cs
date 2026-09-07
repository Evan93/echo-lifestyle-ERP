using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Purchasing.Receiving;

/// <summary>
/// Reading receipts back. Separate from posting because the two have nothing in
/// common beyond the table they touch.
/// </summary>
public partial class GoodsReceiptService
{
    public async Task<PagedResult<GoodsReceiptListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        long? supplierId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.GoodsReceipts.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        if (supplierId is not null)
        {
            query = query.Where(r => r.SupplierId == supplierId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r =>
                EF.Functions.Like(r.Number, $"%{term}%")
                || EF.Functions.Like(r.Supplier!.Name, $"%{term}%")
                || (r.SupplierInvoiceNumber != null
                    && EF.Functions.Like(r.SupplierInvoiceNumber, $"%{term}%"))
                || r.Lines.Any(l => EF.Functions.Like(l.ProductVariant!.Sku, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("number", false) => query.OrderBy(r => r.Number),
            ("supplier", false) => query.OrderBy(r => r.Supplier!.Name).ThenByDescending(r => r.Number),
            ("supplier", true) => query.OrderByDescending(r => r.Supplier!.Name).ThenByDescending(r => r.Number),
            ("date", false) => query.OrderBy(r => r.ReceiptDate).ThenBy(r => r.Number),
            ("date", true) => query.OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.Number),
            ("total", false) => query.OrderBy(r => r.GrandTotal),
            ("total", true) => query.OrderByDescending(r => r.GrandTotal),

            // Newest first by default: the receipt somebody wants is almost
            // always the one just posted.
            _ => query.OrderByDescending(r => r.Number),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(r => new GoodsReceiptListItem
            {
                Id = r.Id,
                Number = r.Number,
                SupplierName = r.Supplier!.Name,
                WarehouseName = r.Warehouse!.Name,
                ReceiptDate = r.ReceiptDate,
                Status = r.Status,
                LineCount = r.Lines.Count,
                TotalQuantity = r.Lines.Sum(l => (decimal?)l.Quantity) ?? 0m,
                GrandTotal = r.GrandTotal,
                SupplierInvoiceNumber = r.SupplierInvoiceNumber,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<GoodsReceiptListItem>(rows, totalCount, filteredCount);
    }

    public async Task<GoodsReceiptDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var receipt = await _db.GoodsReceipts
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v!.Product)
            .Include(r => r.Lines).ThenInclude(l => l.StockBatch)
            .Include(r => r.Charges)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (receipt is null)
        {
            return null;
        }

        return new GoodsReceiptDetail
        {
            Id = receipt.Id,
            Number = receipt.Number,
            SupplierId = receipt.SupplierId,
            SupplierName = receipt.Supplier?.Name ?? string.Empty,
            SupplierIsImporter = receipt.Supplier?.IsImporter ?? false,
            WarehouseId = receipt.WarehouseId,
            WarehouseName = receipt.Warehouse?.Name ?? string.Empty,
            ReceiptDate = receipt.ReceiptDate,
            Status = receipt.Status,
            CurrencyCode = receipt.CurrencyCode,
            ExchangeRate = receipt.ExchangeRate,
            SupplierInvoiceNumber = receipt.SupplierInvoiceNumber,
            SupplierInvoiceDate = receipt.SupplierInvoiceDate,
            Notes = receipt.Notes,
            SubTotal = receipt.SubTotal,
            ChargeTotal = receipt.ChargeTotal,
            GrandTotal = receipt.GrandTotal,
            PostedAtUtc = receipt.PostedAtUtc,
            Lines = receipt.Lines
                .OrderBy(l => l.Id)
                .Select(l => new GoodsReceiptLineDetail
                {
                    Id = l.Id,
                    ProductVariantId = l.ProductVariantId,
                    ProductName = l.ProductVariant?.Product?.Name ?? string.Empty,
                    VariantName = l.ProductVariant?.VariantName ?? string.Empty,
                    Sku = l.ProductVariant?.Sku ?? string.Empty,
                    Quantity = l.Quantity,
                    UnitCost = l.UnitCost,
                    DiscountAmount = l.DiscountAmount,
                    LineTotal = l.LineTotal,
                    LineTotalBase = l.LineTotalBase,
                    ApportionedCharge = l.ApportionedCharge,
                    LandedUnitCost = l.LandedUnitCost,
                    BatchNumber = l.BatchNumber,

                    // Generated numbers are hidden: the whole point of light
                    // batch entry is that nobody sees a number they did not ask
                    // for.
                    BatchWasGenerated = l.StockBatch?.IsAutoGenerated ?? false,
                    ExpiryDate = l.ExpiryDate,
                })
                .ToList(),
            Charges = receipt.Charges
                .OrderBy(c => c.Id)
                .Select(c => new GoodsReceiptChargeDetail
                {
                    ChargeType = c.ChargeType,
                    Description = c.Description,
                    Amount = c.Amount,
                    ApportionMethod = c.ApportionMethod,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Warehouses the given branch draws from, primary first, falling back to
    /// every active warehouse when the branch has no links yet.
    /// </summary>
    public async Task<IReadOnlyList<WarehouseOption>> GetWarehouseOptionsAsync(
        long branchId,
        CancellationToken cancellationToken = default)
    {
        var linked = await _db.BranchWarehouses
            .AsNoTracking()
            .Where(bw => bw.BranchId == branchId && bw.Warehouse!.IsActive)
            .OrderByDescending(bw => bw.IsPrimary)
            .ThenBy(bw => bw.Priority)
            .Select(bw => new WarehouseOption
            {
                Id = bw.WarehouseId,
                Name = bw.Warehouse!.Name,
                Code = bw.Warehouse.Code,
                IsPrimary = bw.IsPrimary,
            })
            .ToListAsync(cancellationToken);

        if (linked.Count > 0)
        {
            return linked;
        }

        return await _db.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseOption
            {
                Id = w.Id,
                Name = w.Name,
                Code = w.Code,
                IsPrimary = false,
            })
            .ToListAsync(cancellationToken);
    }
}

public class WarehouseOption
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
}
