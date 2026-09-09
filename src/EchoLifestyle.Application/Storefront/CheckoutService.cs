using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// Turning a basket into an order.
///
/// The order lands as a <b>draft</b>. Nothing is reserved and nothing moves
/// until somebody at Echo confirms it, which is the same gate a Messenger order
/// goes through - so a joke order or a customer who never answers the phone
/// cannot tie up real stock.
///
/// Everything is decided here on the server: the branch, the warehouse, the
/// prices and the delivery charge. The browser posts a name, a number, an
/// address and nothing else that costs money.
/// </summary>
public class CheckoutService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly CartService _carts;
    private readonly SalesOrderService _orders;
    private readonly CustomerAdminService _customers;

    public CheckoutService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        CartService carts,
        SalesOrderService orders,
        CustomerAdminService customers)
    {
        _db = db;
        _clock = clock;
        _carts = carts;
        _orders = orders;
        _customers = customers;
    }

    /// <summary>
    /// What delivery costs to a given district.
    ///
    /// Two rates, from the company record, chosen by the district's own
    /// inside-city flag - which is reference data, not something the browser
    /// gets to assert. Posting <c>districtId</c> for Dhaka and a Rangpur
    /// address still gets charged as Dhaka, which is why the address is
    /// snapshotted from the same district row that priced it.
    /// </summary>
    public async Task<DeliveryQuote> QuoteDeliveryAsync(
        long districtId,
        decimal orderValue,
        CancellationToken cancellationToken = default)
    {
        var insideCity = await _db.Districts
            .AsNoTracking()
            .Where(d => d.Id == districtId)
            .Select(d => (bool?)d.IsInsideCity)
            .FirstOrDefaultAsync(cancellationToken) ?? false;

        var company = await _db.Companies
            .AsNoTracking()
            .Select(c => new
            {
                c.DeliveryChargeInsideCity,
                c.DeliveryChargeOutsideCity,
                c.FreeDeliveryOverAmount,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var standard = insideCity
            ? company?.DeliveryChargeInsideCity ?? 60m
            : company?.DeliveryChargeOutsideCity ?? 120m;

        var threshold = company?.FreeDeliveryOverAmount;
        var free = threshold is not null && threshold > 0m && orderValue >= threshold;

        return new DeliveryQuote
        {
            IsInsideCity = insideCity,
            StandardCharge = standard,
            IsFree = free,
            Charge = free ? 0m : standard,
        };
    }

    /// <summary>
    /// Places the order.
    /// </summary>
    public async Task<OperationResult<PlacedOrder>> PlaceAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---- what the shopper typed ---------------------------------------

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Fail("Please tell us who the parcel is for.", nameof(request.FullName));
        }

        var phone = BangladeshPhone.Normalise(request.Phone);

        if (phone is null)
        {
            return Fail(
                "That does not look like a Bangladeshi mobile number. It should start 013 to 019.",
                nameof(request.Phone));
        }

        if (string.IsNullOrWhiteSpace(request.AddressLine))
        {
            return Fail("We need a street address to deliver to.", nameof(request.AddressLine));
        }

        if (string.IsNullOrWhiteSpace(request.AreaOrThana))
        {
            return Fail("Which area or thana?", nameof(request.AreaOrThana));
        }

        var district = await _db.Districts
            .AsNoTracking()
            .Include(d => d.Division)
            .FirstOrDefaultAsync(d => d.Id == request.DistrictId && d.IsActive, cancellationToken);

        if (district is null)
        {
            return Fail("Please choose your district.", nameof(request.DistrictId));
        }

        // ---- the basket ----------------------------------------------------

        var cart = await _carts.GetAsync(request.CartToken, cancellationToken);

        if (cart.IsEmpty)
        {
            return Fail("Your basket is empty.");
        }

        if (!cart.CanCheckOut)
        {
            return Fail(
                "Something in your basket is no longer available. Please check it and try again.");
        }

        // ---- where the order belongs, decided here and not by the browser --

        var branch = await _db.Branches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (branch is null)
        {
            return Fail("Ordering is unavailable right now. Please message us instead.");
        }

        var warehouseId = await _db.BranchWarehouses
            .AsNoTracking()
            .Where(bw => bw.BranchId == branch.Id && bw.IsPrimary)
            .Select(bw => bw.WarehouseId)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouseId == 0)
        {
            return Fail("Ordering is unavailable right now. Please message us instead.");
        }

        var delivery = await QuoteDeliveryAsync(district.Id, cart.Subtotal, cancellationToken);

        // ---- who this is ---------------------------------------------------

        var customerId = await ResolveCustomerAsync(request, phone, cancellationToken);

        if (!customerId.Succeeded)
        {
            return OperationResult<PlacedOrder>.Failure(customerId.Error!, customerId.Field);
        }

        var addressId = await SaveAddressAsync(
            customerId.Value, request, district.Id, cancellationToken);

        // ---- the order -----------------------------------------------------

        var created = await _orders.CreateAsync(
            new SaveOrderRequest
            {
                CustomerId = customerId.Value,
                CustomerAddressId = addressId,
                BranchId = branch.Id,
                WarehouseId = warehouseId,
                OrderDate = _clock.ToBusinessDate(_clock.UtcNow),
                Channel = SalesChannel.Website,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                DeliveryCharge = delivery.Charge,
                Notes = string.IsNullOrWhiteSpace(request.DeliveryNotes)
                    ? "Placed on the website."
                    : $"Placed on the website. Customer note: {request.DeliveryNotes.Trim()}",

                // No prices. The server takes them from the price list, so a
                // tampered form cannot buy anything at its own figure.
                Lines = cart.Lines
                    .Select(l => new OrderLineInput
                    {
                        ProductVariantId = l.ProductVariantId,
                        Quantity = l.Quantity,
                    })
                    .ToList(),
            },
            cancellationToken);

        if (!created.Succeeded)
        {
            return OperationResult<PlacedOrder>.Failure(
                "We could not place that order. Please message us and we will sort it out.");
        }

        await CloseCartAsync(cart.Id, customerId.Value, created.Value, cancellationToken);

        var placed = await _orders.GetAsync(created.Value, cancellationToken);

        return OperationResult<PlacedOrder>.Success(new PlacedOrder
        {
            SalesOrderId = created.Value,
            Number = placed?.Number ?? string.Empty,
            RecipientName = request.FullName.Trim(),
            GrandTotal = placed?.GrandTotal ?? 0m,
            DeliveryCharge = delivery.Charge,
            DistrictName = district.Name,
        });
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Finds the customer by number, or creates one.
    ///
    /// Silently, in both directions. The page never says "welcome back" and
    /// never says "we don't know you" - either would turn this form into a way
    /// of testing whether a phone number belongs to one of your customers, and
    /// of learning her name. Same reasoning as the single login error message.
    ///
    /// A blocked customer's order is accepted and flagged, not refused. Telling
    /// somebody at the checkout that they are blocked only teaches them to
    /// reorder from a different number.
    /// </summary>
    private async Task<OperationResult<long>> ResolveCustomerAsync(
        PlaceOrderRequest request,
        string phone,
        CancellationToken cancellationToken)
    {
        var existing = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Phone == phone)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return OperationResult<long>.Success(existing.Id);
        }

        var created = await _customers.CreateAsync(
            new SaveCustomerRequest
            {
                FullName = request.FullName.Trim(),
                Phone = phone,
                Source = CustomerSource.Website,
                CustomerType = CustomerType.Retail,
                IsActive = true,
            },
            cancellationToken);

        return created.Succeeded
            ? OperationResult<long>.Success(created.Value)
            : OperationResult<long>.Failure(
                "We could not save your details. Please message us instead.");
    }

    private async Task<long?> SaveAddressAsync(
        long customerId,
        PlaceOrderRequest request,
        long districtId,
        CancellationToken cancellationToken)
    {
        var saved = await _customers.SaveAddressAsync(
            new SaveAddressRequest
            {
                CustomerId = customerId,
                Label = "Home",
                RecipientName = request.FullName.Trim(),
                DistrictId = districtId,
                AreaOrThana = request.AreaOrThana.Trim(),
                AddressLine = request.AddressLine.Trim(),
                Landmark = string.IsNullOrWhiteSpace(request.Landmark) ? null : request.Landmark.Trim(),
                DeliveryNotes = string.IsNullOrWhiteSpace(request.DeliveryNotes)
                    ? null
                    : request.DeliveryNotes.Trim(),
            },
            cancellationToken);

        // The order copies the address onto itself either way (rule 20), so a
        // failure to save it to the customer's address book is not worth
        // stopping the order for.
        return saved.Succeeded ? saved.Value : null;
    }

    private async Task CloseCartAsync(
        long cartId,
        long customerId,
        long salesOrderId,
        CancellationToken cancellationToken)
    {
        var cart = await _db.Carts.FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);

        if (cart is null)
        {
            return;
        }

        cart.ConvertedToSalesOrderId = salesOrderId;
        cart.CustomerId = customerId;
        cart.LastTouchedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static OperationResult<PlacedOrder> Fail(string error, string? field = null) =>
        OperationResult<PlacedOrder>.Failure(error, field);
}
