using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Application.Finance;
using EchoLifestyle.Application.Inventory;
using EchoLifestyle.Application.Sales.Pricing;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Sales.Orders;

/// <summary>
/// Taking and editing orders.
///
/// An order is a draft until somebody confirms it, and a draft holds nothing: no
/// stock reserved, no promise made. That matters in a business where orders
/// arrive as messages and a good share of them evaporate before the customer
/// says yes. Committing stock the moment a conversation starts would mean the
/// shelf is spoken for by people who never buy.
///
/// The lifecycle after confirmation lives in the other half of this class.
/// </summary>
public partial class SalesOrderService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly PriceResolver _prices;
    private readonly StockReservationService _reservations;
    private readonly StockMovementWriter _movements;
    private readonly CashTransactionWriter _cash;

    public SalesOrderService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditLogger audit,
        PriceResolver prices,
        StockReservationService reservations,
        StockMovementWriter movements,
        CashTransactionWriter cash)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _prices = prices;
        _reservations = reservations;
        _movements = movements;
        _cash = cash;
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, existing: null, cancellationToken);

        if (!prepared.Succeeded)
        {
            return OperationResult<long>.Failure(prepared.Error!, prepared.Field);
        }

        var plan = prepared.Value!;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var order = new SalesOrder
                {
                    Number = await NextNumberAsync(plan.OrderDate, token),
                    Status = SalesOrderStatus.Draft,
                };

                Apply(order, plan);

                _db.SalesOrders.Add(order);
                await _db.SaveChangesAsync(token);

                AddLines(order, plan);
                await _db.SaveChangesAsync(token);

                return OperationResult<long>.Success(order.Id);
            },
            cancellationToken);
    }

    /// <summary>
    /// Edits a draft.
    ///
    /// Only a draft. Once stock is reserved the lines are a promise somebody
    /// made, and quietly changing them underneath a reservation would leave the
    /// reserved quantity describing an order that no longer exists.
    /// </summary>
    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(id, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (!SalesOrder.IsEditable(order.Status))
        {
            return OperationResult.Failure(
                $"{order.Number} is {Describe(order.Status)} and can no longer be edited. "
                + "Cancel it and write a new one, or return it if it has already shipped.");
        }

        var prepared = await PrepareAsync(request, order, cancellationToken);

        if (!prepared.Succeeded)
        {
            return OperationResult.Failure(prepared.Error!, prepared.Field);
        }

        var plan = prepared.Value!;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                Apply(order, plan);

                // Replaced wholesale rather than reconciled line by line. A
                // draft has no reservations and no ledger entries pointing at
                // its lines, so nothing depends on their identity.
                _db.SalesOrderLines.RemoveRange(order.Lines);
                order.Lines.Clear();
                await _db.SaveChangesAsync(token);

                AddLines(order, plan);
                await _db.SaveChangesAsync(token);

                return OperationResult.Success();
            },
            cancellationToken);
    }

    /// <summary>Deletes a draft outright. Nothing has happened, so nothing is reversed.</summary>
    public async Task<OperationResult> DeleteDraftAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(id, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (order.Status != SalesOrderStatus.Draft)
        {
            return OperationResult.Failure(
                $"{order.Number} is {Describe(order.Status)}. Only a draft can be deleted - anything "
                + "further along is cancelled or returned, so the history survives.");
        }

        _db.SalesOrderLines.RemoveRange(order.Lines);
        _db.SalesOrders.Remove(order);

        await _audit.LogAsync(
            AuditActions.SalesOrderDeleted,
            nameof(SalesOrder),
            order.Id.ToString(CultureInfo.InvariantCulture),
            $"Deleted draft {order.Number}.",
            new { order.Number },
            order.BranchId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------
    // Validation and pricing - no writes
    // -----------------------------------------------------------------------

    private async Task<OperationResult<OrderPlan>> PrepareAsync(
        SaveOrderRequest request,
        SalesOrder? existing,
        CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
        {
            return Fail("Add at least one product - an order with nothing in it sells nothing.");
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == request.CustomerId)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.FullName,
                c.Phone,
                c.IsActive,
                c.IsBlocked,
                c.BlockReason,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return Fail("Choose a customer.", nameof(SaveOrderRequest.CustomerId));
        }

        if (!customer.IsActive)
        {
            return Fail(
                $"{customer.FullName} is marked inactive. Reactivate them before taking an order.",
                nameof(SaveOrderRequest.CustomerId));
        }

        // A block refusal is feedback, and feedback needs somebody to receive it.
        //
        // Staff are stopped here, with the reason, because they can act on it -
        // a warning they can click past is not a block. A website visitor is
        // not, and deliberately so: telling somebody at a public checkout that
        // they are blocked only teaches them to reorder from a new number, and
        // there is no staff member present to be told anything.
        //
        // Nothing is lost by letting that draft exist. It reserves no stock and
        // moves nothing, ConfirmAsync refuses it with the reason, and the order
        // carries CustomerIsBlocked so the list shows it for what it is. The
        // block stops the commitment, which is the point of it; it does not
        // have to stop the enquiry being recorded.
        if (customer.IsBlocked && _currentUser.IsStaff)
        {
            return Fail(
                $"{customer.FullName} is blocked: {customer.BlockReason} "
                + "Unblock them first if this order should go ahead.",
                nameof(SaveOrderRequest.CustomerId));
        }

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.IsActive, cancellationToken);

        if (warehouse is null)
        {
            return Fail("Choose an active warehouse to ship from.", nameof(SaveOrderRequest.WarehouseId));
        }

        if (!await _db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, cancellationToken))
        {
            return Fail("Choose an active branch.", nameof(SaveOrderRequest.BranchId));
        }

        // Constrains staff, not the absence of them. This check exists to stop a
        // salesperson writing orders against a branch they do not work in; a
        // website order has no acting staff member to constrain, and its branch
        // is chosen by the server rather than asserted by the caller. Testing
        // IsOwner alone would refuse every order the storefront ever places.
        if (_currentUser.IsStaff && !_currentUser.IsOwner
            && !_currentUser.CanAccessBranch(request.BranchId))
        {
            return Fail("You cannot take orders for that branch.", nameof(SaveOrderRequest.BranchId));
        }

        var address = await ResolveAddressAsync(customer.Id, request.CustomerAddressId, cancellationToken);

        if (address is null)
        {
            return Fail(
                $"{customer.FullName} has no delivery address. Add one on their record first - "
                + "nothing can be shipped without a district for the courier to price.",
                nameof(SaveOrderRequest.CustomerAddressId));
        }

        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var orderDate = request.OrderDate ?? today;

        if (orderDate > today)
        {
            return Fail(
                "An order cannot be dated in the future.", nameof(SaveOrderRequest.OrderDate));
        }

        if (request.DiscountAmount < 0m || request.DeliveryCharge < 0m)
        {
            return Fail("A discount or delivery charge cannot be negative.");
        }

        var variantIds = request.Lines.Select(l => l.ProductVariantId).Distinct().ToList();

        var variants = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.Sku,
                v.VariantName,
                v.IsActive,
                ProductName = v.Product!.Name,
            })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var listPrices = await _prices.ResolveAsync(variantIds, customer.Id, cancellationToken);

        var plan = new OrderPlan
        {
            CustomerId = customer.Id,
            CustomerAddressId = address.Id,
            BranchId = request.BranchId,
            WarehouseId = warehouse.Id,
            OrderDate = orderDate,
            Channel = request.Channel,
            PaymentMethod = request.PaymentMethod,
            Address = address,
            DiscountAmount = decimal.Round(request.DiscountAmount, 4, MidpointRounding.AwayFromZero),
            DeliveryCharge = decimal.Round(request.DeliveryCharge, 4, MidpointRounding.AwayFromZero),
            Notes = Trim(request.Notes),
        };

        var seen = new HashSet<long>();

        foreach (var input in request.Lines)
        {
            if (!variants.TryGetValue(input.ProductVariantId, out var variant))
            {
                return Fail("One of the products on this order no longer exists.");
            }

            if (!variant.IsActive)
            {
                return Fail($"{variant.Sku} is no longer for sale.");
            }

            if (input.Quantity <= 0m)
            {
                return Fail($"Enter a quantity greater than zero for {variant.Sku}.");
            }

            if (!seen.Add(input.ProductVariantId))
            {
                return Fail(
                    $"{variant.Sku} appears twice. Combine those lines - two lines of the same "
                    + "product make the total harder to check and the packing list wrong.");
            }

            // The typed price wins where there is one. Somebody agreed a figure
            // in a conversation, and the system should record what was agreed
            // rather than what it would have preferred.
            var unitPrice = input.UnitPrice ?? listPrices.GetValueOrDefault(input.ProductVariantId);

            if (unitPrice <= 0m)
            {
                return Fail(
                    $"{variant.Sku} has no selling price. Set one on the product, or type the price "
                    + "agreed with the customer.");
            }

            if (input.DiscountAmount < 0m)
            {
                return Fail($"The discount on {variant.Sku} cannot be negative.");
            }

            var lineTotal = decimal.Round(
                (input.Quantity * unitPrice) - input.DiscountAmount, 4, MidpointRounding.AwayFromZero);

            if (lineTotal < 0m)
            {
                return Fail($"The discount on {variant.Sku} is larger than the line itself.");
            }

            plan.Lines.Add(new PlannedOrderLine
            {
                ProductVariantId = variant.Id,
                Sku = variant.Sku,
                ProductName = variant.ProductName,
                VariantName = variant.VariantName,
                Quantity = input.Quantity,
                UnitPrice = unitPrice,
                DiscountAmount = input.DiscountAmount,
                LineTotal = lineTotal,
                Notes = Trim(input.Notes),
            });
        }

        plan.SubTotal = plan.Lines.Sum(l => l.LineTotal);

        if (plan.DiscountAmount > plan.SubTotal)
        {
            return Fail(
                "The order discount is larger than the order. Reduce it, or discount the lines "
                + "individually.",
                nameof(SaveOrderRequest.DiscountAmount));
        }

        // Availability is not checked here. A draft promises nothing, and
        // refusing to write one down because stock is short would lose an order
        // that a delivery tomorrow would have filled. Confirmation is where the
        // promise is made, and where availability is enforced.
        return OperationResult<OrderPlan>.Success(plan);
    }

    /// <summary>
    /// The address to ship to: the one asked for, or the customer's default.
    /// </summary>
    private async Task<AddressSnapshot?> ResolveAddressAsync(
        long customerId,
        long? addressId,
        CancellationToken cancellationToken)
    {
        var query = _db.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId);

        query = addressId is not null
            ? query.Where(a => a.Id == addressId)
            : query.OrderByDescending(a => a.IsDefault);

        return await query
            .Select(a => new AddressSnapshot
            {
                Id = a.Id,
                RecipientName = a.RecipientName,
                RecipientPhone = a.RecipientPhone,
                DivisionName = a.Division!.Name,
                DistrictName = a.District!.Name,
                IsInsideCity = a.District.IsInsideCity,
                AreaOrThana = a.AreaOrThana,
                AddressLine = a.AddressLine,
                Landmark = a.Landmark,
                PostCode = a.PostCode,
                DeliveryNotes = a.DeliveryNotes,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Writing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Copies the plan onto the order, address and all.
    ///
    /// The address is written as values, not as a foreign key. A customer who
    /// moves next month must not rewrite where this parcel went, and when a
    /// courier disputes a delivery the only useful answer is the address as it
    /// was on the label.
    /// </summary>
    private static void Apply(SalesOrder order, OrderPlan plan)
    {
        order.CustomerId = plan.CustomerId;
        order.CustomerAddressId = plan.CustomerAddressId;
        order.BranchId = plan.BranchId;
        order.WarehouseId = plan.WarehouseId;
        order.OrderDate = plan.OrderDate;
        order.Channel = plan.Channel;
        order.PaymentMethod = plan.PaymentMethod;

        order.RecipientName = plan.Address.RecipientName;
        order.RecipientPhone = plan.Address.RecipientPhone;
        order.DivisionName = plan.Address.DivisionName;
        order.DistrictName = plan.Address.DistrictName;
        order.AreaOrThana = plan.Address.AreaOrThana;
        order.AddressLine = plan.Address.AddressLine;
        order.Landmark = plan.Address.Landmark;
        order.PostCode = plan.Address.PostCode;
        order.DeliveryNotes = plan.Address.DeliveryNotes;
        order.IsInsideCity = plan.Address.IsInsideCity;

        order.SubTotal = plan.SubTotal;
        order.DiscountAmount = plan.DiscountAmount;
        order.DeliveryCharge = plan.DeliveryCharge;
        order.GrandTotal = plan.SubTotal - plan.DiscountAmount + plan.DeliveryCharge;
        order.Notes = plan.Notes;
    }

    private static void AddLines(SalesOrder order, OrderPlan plan)
    {
        foreach (var line in plan.Lines)
        {
            order.Lines.Add(new SalesOrderLine
            {
                SalesOrderId = order.Id,
                ProductVariantId = line.ProductVariantId,

                // Snapshots. A product renamed next season must not change what
                // a past invoice says was sold.
                Sku = line.Sku,
                ProductName = line.ProductName,
                VariantName = line.VariantName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountAmount = line.DiscountAmount,
                LineTotal = line.LineTotal,

                // Cost is unknown until dispatch decides which batch ships.
                CostOfGoods = 0m,
                Notes = line.Notes,
            });
        }
    }

    private Task<SalesOrder?> LoadAsync(long id, CancellationToken cancellationToken) =>
        _db.SalesOrders
            .Include(o => o.Lines)
            .Include(o => o.Reservations)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    private async Task<string> NextNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = DocumentNumber.Prefix(DocumentNumber.SalesOrder, date);

        var used = await _db.SalesOrders
            .Where(o => o.Number.StartsWith(prefix))
            .Select(o => o.Number)
            .ToListAsync(cancellationToken);

        return DocumentNumber.Next(prefix, used);
    }

    internal static string Describe(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Draft => "still a draft",
        SalesOrderStatus.Confirmed => "confirmed",
        SalesOrderStatus.Packed => "packed",
        SalesOrderStatus.Dispatched => "with the courier",
        SalesOrderStatus.Delivered => "delivered",
        SalesOrderStatus.Cancelled => "cancelled",
        SalesOrderStatus.Returned => "returned",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static OperationResult<OrderPlan> Fail(string error, string? field = null) =>
        OperationResult<OrderPlan>.Failure(error, field);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // -----------------------------------------------------------------------
    // Everything decided before the first write
    // -----------------------------------------------------------------------

    private sealed class OrderPlan
    {
        public required long CustomerId { get; init; }

        public required long? CustomerAddressId { get; init; }

        public required long BranchId { get; init; }

        public required long WarehouseId { get; init; }

        public required DateOnly OrderDate { get; init; }

        public required SalesChannel Channel { get; init; }

        public required PaymentMethod PaymentMethod { get; init; }

        public required AddressSnapshot Address { get; init; }

        public required decimal DiscountAmount { get; init; }

        public required decimal DeliveryCharge { get; init; }

        public string? Notes { get; init; }

        public List<PlannedOrderLine> Lines { get; } = [];

        public decimal SubTotal { get; set; }
    }

    private sealed class PlannedOrderLine
    {
        public required long ProductVariantId { get; init; }

        public required string Sku { get; init; }

        public required string ProductName { get; init; }

        public required string VariantName { get; init; }

        public required decimal Quantity { get; init; }

        public required decimal UnitPrice { get; init; }

        public required decimal DiscountAmount { get; init; }

        public required decimal LineTotal { get; init; }

        public string? Notes { get; init; }
    }

    private sealed class AddressSnapshot
    {
        public long Id { get; init; }

        public string RecipientName { get; init; } = string.Empty;

        public string RecipientPhone { get; init; } = string.Empty;

        public string DivisionName { get; init; } = string.Empty;

        public string DistrictName { get; init; } = string.Empty;

        public bool IsInsideCity { get; init; }

        public string AreaOrThana { get; init; } = string.Empty;

        public string AddressLine { get; init; } = string.Empty;

        public string? Landmark { get; init; }

        public string? PostCode { get; init; }

        public string? DeliveryNotes { get; init; }
    }
}
