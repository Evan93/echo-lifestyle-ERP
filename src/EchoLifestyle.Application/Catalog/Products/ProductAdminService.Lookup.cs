using EchoLifestyle.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Catalog.Products;

/// <summary>
/// Finding a variant quickly, for screens that add lines by typing.
///
/// Lives here rather than in purchasing because there should be one place that
/// knows how to search the catalogue - sales, stock counts and transfers will
/// all need the same thing, and three near-identical queries would drift.
/// </summary>
public partial class ProductAdminService
{
    /// <summary>
    /// Variants matching a term, ordered so an exact SKU or barcode wins.
    ///
    /// Someone scanning a barcode or typing a full SKU expects one hit at the
    /// top; someone typing "cera" expects a list. Both are the same query, and
    /// the ordering is what makes it feel like two.
    /// </summary>
    public async Task<IReadOnlyList<VariantLookupItem>> LookupVariantsAsync(
        string? term,
        int take = 15,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        var search = term.Trim();
        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        var query = _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive
                        && v.Product!.IsActive
                        && (EF.Functions.Like(v.Sku, $"%{search}%")
                            || (v.Barcode != null && EF.Functions.Like(v.Barcode, $"%{search}%"))
                            || EF.Functions.Like(v.Product.Name, $"%{search}%")
                            || EF.Functions.Like(v.Product.Code, $"%{search}%")
                            || EF.Functions.Like(v.Product.Brand!.Name, $"%{search}%")));

        var rows = await query
            .Select(v => new VariantLookupItem
            {
                Id = v.Id,
                Sku = v.Sku,
                Barcode = v.Barcode,
                ProductName = v.Product!.Name,
                BrandName = v.Product.Brand!.Name,
                VariantName = v.VariantName,
                IsBatchTracked = v.Product.IsBatchTracked,
                IsExpiryTracked = v.Product.IsExpiryTracked,
                ShelfLifeDays = v.Product.ShelfLifeDays,

                // What it was last bought for, so the cost field can be
                // pre-filled instead of remembered.
                LastUnitCost = _db.GoodsReceiptLines
                    .Where(l => l.ProductVariantId == v.Id)
                    .OrderByDescending(l => l.Id)
                    .Select(l => (decimal?)l.UnitCost)
                    .FirstOrDefault(),

                CurrentPrice = _db.PriceListItems
                    .Where(i => i.ProductVariantId == v.Id
                                && i.PriceListId == priceListId
                                && i.EffectiveToUtc == null)
                    .Select(i => (decimal?)i.UnitPrice)
                    .FirstOrDefault(),

                // Exact matches first: a scanned barcode should not land third
                // behind two products whose names happen to contain the digits.
                Rank = v.Sku == search ? 0
                    : v.Barcode == search ? 0
                    : v.Sku.StartsWith(search) ? 1
                    : v.Product.Name.StartsWith(search) ? 2
                    : 3,
            })
            .OrderBy(v => v.Rank)
            .ThenBy(v => v.ProductName)
            .ThenBy(v => v.VariantName)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows;
    }
}

public class VariantLookupItem
{
    public long Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public bool IsBatchTracked { get; set; }

    public bool IsExpiryTracked { get; set; }

    public int? ShelfLifeDays { get; set; }

    public decimal? LastUnitCost { get; set; }

    public decimal? CurrentPrice { get; set; }

    /// <summary>Ordering only - not meaningful to the caller.</summary>
    public int Rank { get; set; }

    /// <summary>
    /// "Cetaphil Gentle Cleanser - 250ml" - the product plus its variant, unless
    /// the product does not vary.
    /// </summary>
    public string Display => VariantName == ProductAdminService.DefaultVariantName
        ? ProductName
        : $"{ProductName} - {VariantName}";
}
