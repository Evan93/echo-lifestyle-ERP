using System.Security.Cryptography;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// The basket.
///
/// Every read prices the lines again from the price list and re-checks
/// availability, so a basket cannot hold a stale price or promise stock that
/// has since gone. Nothing here reserves anything - see <see cref="Cart"/> for
/// why that is deliberate rather than an omission.
/// </summary>
public class CartService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CartService(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// A new cart token.
    ///
    /// 32 random bytes, not a sequential id and not a GUID derived from a
    /// timestamp: this string is the only thing standing between one shopper's
    /// basket and another's, so it has to be unguessable rather than merely
    /// unique.
    /// </summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>
    /// Reads a basket by its cookie token, priced and checked.
    /// Returns an empty basket for an unknown or already-converted token
    /// rather than an error - a stale cookie is not a fault.
    /// </summary>
    public async Task<ShopCart> GetAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShopCart();
        }

        var cart = await _db.Carts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Token == token && c.ConvertedToSalesOrderId == null, cancellationToken);

        if (cart is null)
        {
            return new ShopCart();
        }

        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        var lines = await _db.CartLines
            .AsNoTracking()
            .Where(l => l.CartId == cart.Id)
            .OrderBy(l => l.AddedAtUtc)
            .Select(l => new ShopCartLine
            {
                Id = l.Id,
                ProductVariantId = l.ProductVariantId,
                Quantity = l.Quantity,
                ProductId = l.ProductVariant!.ProductId,
                ProductName = l.ProductVariant.Product!.Name,
                ProductSlug = l.ProductVariant.Product.Slug,
                BrandName = l.ProductVariant.Product.Brand!.Name,
                Sku = l.ProductVariant.Sku,

                // Blank on a single-variant product, where "Default" would be
                // noise under the product's own name.
                VariantName = l.ProductVariant.Product.Variants.Count(v => v.IsActive) > 1
                    ? l.ProductVariant.VariantName
                    : null,

                // The same rule the catalogue uses. A product pulled from sale
                // while it sat in somebody's basket must not check out.
                IsSellable = l.ProductVariant.IsActive
                             && l.ProductVariant.Product.IsActive
                             && l.ProductVariant.Product.IsPublished
                             && l.ProductVariant.Product.Brand.IsActive,

                UnitPrice = _db.PriceListItems
                    .Where(i => i.ProductVariantId == l.ProductVariantId
                                && i.PriceListId == priceListId
                                && i.EffectiveToUtc == null)
                    .Select(i => (decimal?)i.UnitPrice)
                    .FirstOrDefault(),

                Available = _db.StockBalances
                    .Where(b => b.ProductVariantId == l.ProductVariantId)
                    .Sum(b => (decimal?)(b.QuantityOnHand - b.QuantityReserved)) ?? 0m,

                ImagePath = l.ProductVariant.Product.Images
                    .OrderByDescending(i => i.ProductVariantId == l.ProductVariantId)
                    .ThenByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.Path)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new ShopCart { Id = cart.Id, Token = cart.Token, Lines = lines };
    }

    /// <summary>
    /// Adds a variant, or raises the quantity if it is already there.
    /// </summary>
    /// <returns>The cart token, which the caller writes back to the cookie.</returns>
    public async Task<OperationResult<string>> AddAsync(
        string? token,
        long productVariantId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            quantity = 1m;
        }

        quantity = Math.Floor(quantity);

        var sellable = await _db.ProductVariants
            .AsNoTracking()
            .AnyAsync(
                v => v.Id == productVariantId
                     && v.IsActive
                     && v.Product!.IsActive
                     && v.Product.IsPublished
                     && v.Product.Brand!.IsActive,
                cancellationToken);

        if (!sellable)
        {
            return OperationResult<string>.Failure("That product is not for sale.");
        }

        var cart = await LoadOrStartAsync(token, cancellationToken);

        var line = await _db.CartLines
            .FirstOrDefaultAsync(
                l => l.CartId == cart.Id && l.ProductVariantId == productVariantId,
                cancellationToken);

        if (line is null)
        {
            var lineCount = await _db.CartLines.CountAsync(l => l.CartId == cart.Id, cancellationToken);

            if (lineCount >= Cart.MaxLines)
            {
                return OperationResult<string>.Failure(
                    $"A basket holds {Cart.MaxLines} different products. Please place this order "
                    + "first, or message us and we will take the rest by hand.");
            }

            _db.CartLines.Add(new CartLine
            {
                CartId = cart.Id,
                ProductVariantId = productVariantId,
                Quantity = Math.Min(quantity, Cart.MaxQuantityPerLine),
                AddedAtUtc = _clock.UtcNow,
            });
        }
        else
        {
            line.Quantity = Math.Min(line.Quantity + quantity, Cart.MaxQuantityPerLine);
        }

        cart.LastTouchedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<string>.Success(cart.Token);
    }

    /// <summary>Sets a line's quantity. Zero removes it.</summary>
    public async Task<OperationResult> SetQuantityAsync(
        string? token,
        long cartLineId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        var cart = await TrackedCartAsync(token, cancellationToken);

        if (cart is null)
        {
            return OperationResult.Failure("Your basket has expired.");
        }

        var line = await _db.CartLines
            .FirstOrDefaultAsync(l => l.Id == cartLineId && l.CartId == cart.Id, cancellationToken);

        if (line is null)
        {
            // Somebody else's line id, or one already removed in another tab.
            // Not an error worth showing - the basket is redrawn either way.
            return OperationResult.Success();
        }

        quantity = Math.Floor(quantity);

        if (quantity <= 0m)
        {
            _db.CartLines.Remove(line);
        }
        else
        {
            line.Quantity = Math.Min(quantity, Cart.MaxQuantityPerLine);
        }

        cart.LastTouchedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> RemoveAsync(
        string? token,
        long cartLineId,
        CancellationToken cancellationToken = default) =>
        await SetQuantityAsync(token, cartLineId, 0m, cancellationToken);

    /// <summary>How many items the header badge shows. Deliberately cheap.</summary>
    public async Task<int> CountAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        var total = await _db.CartLines
            .AsNoTracking()
            .Where(l => l.Cart!.Token == token && l.Cart.ConvertedToSalesOrderId == null)
            .SumAsync(l => (decimal?)l.Quantity, cancellationToken);

        return (int)(total ?? 0m);
    }

    // -----------------------------------------------------------------------

    internal async Task<Cart> LoadOrStartAsync(string? token, CancellationToken cancellationToken)
    {
        var cart = await TrackedCartAsync(token, cancellationToken);

        if (cart is not null)
        {
            return cart;
        }

        cart = new Cart
        {
            Token = NewToken(),
            CreatedAtUtc = _clock.UtcNow,
            LastTouchedAtUtc = _clock.UtcNow,
        };

        _db.Carts.Add(cart);
        await _db.SaveChangesAsync(cancellationToken);

        return cart;
    }

    internal async Task<Cart?> TrackedCartAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await _db.Carts
            .FirstOrDefaultAsync(
                c => c.Token == token && c.ConvertedToSalesOrderId == null, cancellationToken);
    }

    internal async Task<long> DefaultPriceListIdAsync(CancellationToken cancellationToken) =>
        await _db.PriceLists
            .AsNoTracking()
            .Where(p => p.IsDefault && p.IsActive)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
