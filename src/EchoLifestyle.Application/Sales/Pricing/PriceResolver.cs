using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Sales.Pricing;

/// <summary>
/// What a given customer pays for a given product, right now.
///
/// One place, because the order screen, the storefront and any future quote all
/// have to agree. Two implementations of "what does this cost" is two answers,
/// and the customer will find the cheaper one.
///
/// The resolved price is a starting point: it is copied onto the order line and
/// frozen there. Changing a price list later must never rewrite what a past
/// order charged.
/// </summary>
public class PriceResolver
{
    private readonly IApplicationDbContext _db;

    public PriceResolver(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Prices several variants at once for one customer.
    ///
    /// Batched deliberately: an order screen prices every line it holds, and
    /// doing that one query at a time is how a ten-line order becomes twenty
    /// round trips.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, decimal>> ResolveAsync(
        IReadOnlyCollection<long> productVariantIds,
        long? customerId,
        CancellationToken cancellationToken = default)
    {
        if (productVariantIds.Count == 0)
        {
            return new Dictionary<long, decimal>();
        }

        var priceListId = await PriceListForAsync(customerId, cancellationToken);
        var defaultListId = await DefaultPriceListIdAsync(cancellationToken);

        var prices = new Dictionary<long, decimal>();

        // The customer's own list first, where they have one.
        if (priceListId is not null && priceListId != defaultListId)
        {
            foreach (var row in await CurrentPricesAsync(
                         productVariantIds, priceListId.Value, cancellationToken))
            {
                prices[row.ProductVariantId] = row.UnitPrice;
            }
        }

        // Retail fills every gap. A wholesale list that does not name a product
        // means "no special price for that one", not "not for sale".
        var missing = productVariantIds.Where(id => !prices.ContainsKey(id)).ToList();

        if (missing.Count > 0 && defaultListId is not null)
        {
            foreach (var row in await CurrentPricesAsync(missing, defaultListId.Value, cancellationToken))
            {
                prices[row.ProductVariantId] = row.UnitPrice;
            }
        }

        return prices;
    }

    public async Task<decimal?> ResolveOneAsync(
        long productVariantId,
        long? customerId,
        CancellationToken cancellationToken = default)
    {
        var prices = await ResolveAsync([productVariantId], customerId, cancellationToken);

        return prices.TryGetValue(productVariantId, out var price) ? price : null;
    }

    /// <summary>
    /// The list a customer buys on: their own if they are wholesale with one
    /// set, otherwise the default.
    /// </summary>
    private async Task<long?> PriceListForAsync(long? customerId, CancellationToken cancellationToken)
    {
        if (customerId is null)
        {
            return await DefaultPriceListIdAsync(cancellationToken);
        }

        var assigned = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == customerId && c.CustomerType == CustomerType.Wholesale)
            .Select(c => c.PriceListId)
            .FirstOrDefaultAsync(cancellationToken);

        return assigned ?? await DefaultPriceListIdAsync(cancellationToken);
    }

    private Task<long?> DefaultPriceListIdAsync(CancellationToken cancellationToken) =>
        _db.PriceLists
            .AsNoTracking()
            .Where(p => p.IsDefault && p.IsActive)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The open price on each variant. A price list item with no end date is the
    /// one in force; ended ones are history and are what let a past order be
    /// explained.
    /// </summary>
    private async Task<List<PriceRow>> CurrentPricesAsync(
        IReadOnlyCollection<long> productVariantIds,
        long priceListId,
        CancellationToken cancellationToken) =>
        await _db.PriceListItems
            .AsNoTracking()
            .Where(i => i.PriceListId == priceListId
                        && productVariantIds.Contains(i.ProductVariantId)
                        && i.EffectiveToUtc == null)
            .Select(i => new PriceRow
            {
                ProductVariantId = i.ProductVariantId,
                UnitPrice = i.UnitPrice,
            })
            .ToListAsync(cancellationToken);

    private sealed class PriceRow
    {
        public long ProductVariantId { get; init; }

        public decimal UnitPrice { get; init; }
    }
}
