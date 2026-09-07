using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Inventory;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Orders, and the two moments that matter.
///
/// Confirming reserves: available falls, on hand does not, and no ledger entry
/// is written because nothing has moved. Dispatching issues: on hand falls, the
/// ledger says so, and the order finally learns what it cost.
///
/// Everything else here is about the endings - delivered short, refused at the
/// door, cancelled before it shipped - which in a cash-on-delivery business are
/// not edge cases.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SalesOrderServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public SalesOrderServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _fixture.CurrentUser.IsAuthenticated = true;
        _fixture.CurrentUser.UserType = UserType.Staff;
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.UserId = 1;
        _fixture.CurrentUser.UserName = "test-owner";
        _fixture.CurrentUser.Grants.Clear();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        _branchId = await db.Branches.Where(b => b.IsActive).Select(b => b.Id).FirstAsync();
        _warehouseId = await db.Warehouses.Where(w => w.IsActive).Select(w => w.Id).FirstAsync();

        _fixture.CurrentUser.BranchIds = [_branchId];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniquePhone() =>
        "018" + Random.Shared.Next(10_000_000, 99_999_999).ToString("D8");

    /// <summary>A customer with a delivery address, ready to be ordered for.</summary>
    private async Task<long> CustomerAsync(IServiceProvider services)
    {
        var customers = services.GetRequiredService<CustomerAdminService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var created = await customers.CreateAsync(new SaveCustomerRequest
        {
            FullName = $"Order customer {InventoryTestData.Unique()}",
            Phone = UniquePhone(),
            Source = CustomerSource.Messenger,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        var districtId = await db.Districts.Where(d => d.Name == "Dhaka").Select(d => d.Id).FirstAsync();

        var address = await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = districtId,
            AreaOrThana = "Dhanmondi",
            AddressLine = "House 27, Road 11",
            Landmark = "Beside the pharmacy",
        });

        Assert.True(address.Succeeded, address.Error);

        return created.Value;
    }

    /// <summary>A product with stock on the shelf and a selling price.</summary>
    private async Task<(long VariantId, string Sku)> StockedProductAsync(
        IServiceProvider services,
        decimal quantity = 10m,
        decimal unitCost = 200m)
    {
        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(services, suffix);
        var variant = await InventoryTestData.VariantAsync(services, suffix);

        await InventoryTestData.ReceiveAsync(
            services, supplierId, _warehouseId, _branchId, variant.VariantId, quantity, unitCost);

        return (variant.VariantId, variant.Sku);
    }

    private SaveOrderRequest Request(long customerId, long variantId, decimal quantity = 2m) => new()
    {
        CustomerId = customerId,
        BranchId = _branchId,
        WarehouseId = _warehouseId,
        OrderDate = new DateOnly(2026, 9, 7),
        Channel = SalesChannel.Messenger,
        DeliveryCharge = 60m,
        Lines = [new OrderLineInput { ProductVariantId = variantId, Quantity = quantity }],
    };

    private Task<decimal> OnHandAsync(EchoDbContext db, long variantId) =>
        db.StockBalances
            .Where(b => b.ProductVariantId == variantId && b.WarehouseId == _warehouseId)
            .SumAsync(b => b.QuantityOnHand);

    private Task<decimal> ReservedAsync(EchoDbContext db, long variantId) =>
        db.StockBalances
            .Where(b => b.ProductVariantId == variantId && b.WarehouseId == _warehouseId)
            .SumAsync(b => b.QuantityReserved);

    // -----------------------------------------------------------------------
    // Drafting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_draft_reserves_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId, 3m));

        Assert.True(created.Succeeded, created.Error);

        // Orders arrive as messages and many evaporate. Committing stock the
        // moment a conversation starts would tie the shelf up for people who
        // never buy.
        Assert.Equal(0m, await ReservedAsync(db, variantId));
        Assert.Equal(10m, await OnHandAsync(db, variantId));
    }

    [Fact]
    public async Task A_draft_can_be_written_for_more_stock_than_exists()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider, quantity: 2m);

        // Refusing here would lose an order that tomorrow's delivery fills.
        var created = await orders.CreateAsync(Request(customerId, variantId, 20m));

        Assert.True(created.Succeeded, created.Error);
    }

    [Fact]
    public async Task The_delivery_address_is_copied_not_referenced()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));
        Assert.True(created.Succeeded, created.Error);

        // The customer moves house.
        var address = (await customers.GetAsync(customerId))!.Addresses.Single();
        var districtId = await db.Districts.Where(d => d.Name == "Gazipur").Select(d => d.Id).FirstAsync();

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            Id = address.Id,
            CustomerId = customerId,
            DistrictId = districtId,
            AreaOrThana = "Tongi",
            AddressLine = "House 5, Block B",
        });

        var order = await orders.GetAsync(created.Value);

        // The parcel still went where it went. Anything else would rewrite
        // history the moment a courier disputes a delivery.
        Assert.Equal("House 27, Road 11", order!.AddressLine);
        Assert.Equal("Dhaka", order.DistrictName);
    }

    [Fact]
    public async Task An_order_for_a_blocked_customer_is_refused_outright()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        await customers.BlockAsync(customerId, "Refused three parcels.");

        var created = await orders.CreateAsync(Request(customerId, variantId));

        // A warning somebody can click past is not a block.
        Assert.False(created.Succeeded);
        Assert.Contains("blocked", created.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_customer_with_no_address_cannot_be_ordered_for()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(new SaveCustomerRequest
        {
            FullName = "No address",
            Phone = UniquePhone(),
            IsActive = true,
        });

        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var order = await orders.CreateAsync(Request(created.Value, variantId));

        Assert.False(order.Succeeded);
        Assert.Contains("no delivery address", order.Error!);
    }

    [Fact]
    public async Task Totals_are_computed_by_the_server()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var request = Request(customerId, variantId, 3m);
        request.DiscountAmount = 100m;
        request.Lines = [new OrderLineInput
        {
            ProductVariantId = variantId,
            Quantity = 3m,
            UnitPrice = 500m,
            DiscountAmount = 50m,
        }];

        var created = await orders.CreateAsync(request);
        Assert.True(created.Succeeded, created.Error);

        var order = await orders.GetAsync(created.Value);

        // (3 x 500) - 50 = 1450 goods, less 100 order discount, plus 60 delivery.
        Assert.Equal(1450m, order!.SubTotal);
        Assert.Equal(1410m, order.GrandTotal);
    }

    // -----------------------------------------------------------------------
    // Confirming - the promise
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Confirming_reserves_stock_without_moving_it()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider, quantity: 10m);

        var created = await orders.CreateAsync(Request(customerId, variantId, 4m));
        var confirmed = await orders.ConfirmAsync(created.Value);

        Assert.True(confirmed.Succeeded, confirmed.Error);

        // The jar is still on the shelf. It is just spoken for.
        Assert.Equal(10m, await OnHandAsync(db, variantId));
        Assert.Equal(4m, await ReservedAsync(db, variantId));

        // And nothing is in the ledger, because nothing has moved.
        Assert.False(await db.StockLedger.AnyAsync(
            e => e.DocumentType == StockDocumentType.SalesOrder && e.DocumentId == created.Value));
    }

    [Fact]
    public async Task A_second_order_cannot_be_promised_the_same_units()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var first = await CustomerAsync(scope.ServiceProvider);
        var second = await CustomerAsync(scope.ServiceProvider);
        var (variantId, sku) = await StockedProductAsync(scope.ServiceProvider, quantity: 5m);

        var one = await orders.CreateAsync(Request(first, variantId, 4m));
        Assert.True((await orders.ConfirmAsync(one.Value)).Succeeded);

        var two = await orders.CreateAsync(Request(second, variantId, 3m));
        var confirmed = await orders.ConfirmAsync(two.Value);

        Assert.False(confirmed.Succeeded);
        Assert.Contains(sku, confirmed.Error!);
        Assert.Contains("Only 1", confirmed.Error!);

        // The refusal left nothing behind - the second order holds nothing.
        Assert.Equal(4m, await ReservedAsync(db, variantId));
    }

    [Fact]
    public async Task A_partly_fillable_order_reserves_nothing_at_all()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var plenty = await StockedProductAsync(scope.ServiceProvider, quantity: 20m);
        var scarce = await StockedProductAsync(scope.ServiceProvider, quantity: 1m);

        var request = Request(customerId, plenty.VariantId);
        request.Lines =
        [
            new OrderLineInput { ProductVariantId = plenty.VariantId, Quantity = 2m },
            new OrderLineInput { ProductVariantId = scarce.VariantId, Quantity = 5m },
        ];

        var created = await orders.CreateAsync(request);
        var confirmed = await orders.ConfirmAsync(created.Value);

        Assert.False(confirmed.Succeeded);

        // The first line's reservation was rolled back with the transaction.
        // Half a promise is worse than none.
        Assert.Equal(0m, await ReservedAsync(db, plenty.VariantId));
        Assert.Equal(0m, await ReservedAsync(db, scarce.VariantId));
    }

    [Fact]
    public async Task A_confirmed_order_cannot_be_edited()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));
        await orders.ConfirmAsync(created.Value);

        var edited = await orders.UpdateAsync(created.Value, Request(customerId, variantId, 5m));

        Assert.False(edited.Succeeded);
        Assert.Contains("can no longer be edited", edited.Error!);
    }

    // -----------------------------------------------------------------------
    // Dispatching - the movement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dispatching_releases_the_reservation_and_issues_the_stock()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(
            scope.ServiceProvider, quantity: 10m, unitCost: 220m);

        var created = await orders.CreateAsync(Request(customerId, variantId, 3m));
        await orders.ConfirmAsync(created.Value);

        var dispatched = await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Steadfast",
            ConsignmentNumber = "SF-88213",
        });

        Assert.True(dispatched.Succeeded, dispatched.Error);

        // On hand falls, reserved goes back to nothing.
        Assert.Equal(7m, await OnHandAsync(db, variantId));
        Assert.Equal(0m, await ReservedAsync(db, variantId));

        var entry = await db.StockLedger.SingleAsync(
            e => e.DocumentType == StockDocumentType.SalesOrder && e.DocumentId == created.Value);

        Assert.Equal(StockMovementType.Issue, entry.MovementType);
        Assert.Equal(-3m, entry.QuantityChange);
        Assert.Equal(220m, entry.UnitCost);

        // And the order finally knows what it cost.
        var order = await orders.GetAsync(created.Value);
        Assert.Equal(660m, order!.CostOfGoods);
        Assert.Equal("Steadfast", order.CourierName);
    }

    [Fact]
    public async Task Dispatch_takes_the_oldest_batch_first_and_costs_it_accordingly()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(scope.ServiceProvider, suffix);
        var variant = await InventoryTestData.VariantAsync(
            scope.ServiceProvider, suffix, expiryTracked: true);

        // Two batches, the cheaper one expiring sooner.
        await InventoryTestData.ReceiveAsync(
            scope.ServiceProvider, supplierId, _warehouseId, _branchId, variant.VariantId,
            2m, 100m, $"OLD-{suffix}", new DateOnly(2027, 1, 1));

        await InventoryTestData.ReceiveAsync(
            scope.ServiceProvider, supplierId, _warehouseId, _branchId, variant.VariantId,
            10m, 180m, $"NEW-{suffix}", new DateOnly(2028, 1, 1));

        var created = await orders.CreateAsync(Request(customerId, variant.VariantId, 5m));
        await orders.ConfirmAsync(created.Value);

        var dispatched = await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Pathao",
        });

        Assert.True(dispatched.Succeeded, dispatched.Error);

        var entries = await db.StockLedger
            .Where(e => e.DocumentType == StockDocumentType.SalesOrder
                        && e.DocumentId == created.Value)
            .ToListAsync();

        Assert.Equal(2, entries.Count);

        // 2 at 100 out of the batch that expires first, then 3 at 180.
        var order = await orders.GetAsync(created.Value);
        Assert.Equal(740m, order!.CostOfGoods);
    }

    [Fact]
    public async Task Dispatch_needs_a_courier()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));
        await orders.ConfirmAsync(created.Value);

        var dispatched = await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "   ",
        });

        Assert.False(dispatched.Succeeded);
    }

    [Fact]
    public async Task A_draft_cannot_be_dispatched()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));

        var dispatched = await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "RedX",
        });

        Assert.False(dispatched.Succeeded);
    }

    // -----------------------------------------------------------------------
    // Endings
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_short_collection_is_recorded_as_it_is()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId, 2m));
        await orders.ConfirmAsync(created.Value);
        await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Steadfast",
        });

        var before = await orders.GetAsync(created.Value);

        var delivered = await orders.MarkDeliveredAsync(new DeliveryRequest
        {
            OrderId = created.Value,
            AmountCollected = before!.GrandTotal - 60m,
            Note = "Courier deducted their fee.",
        });

        Assert.True(delivered.Succeeded, delivered.Error);

        var after = await orders.GetAsync(created.Value);

        // Rounding it up to the total would lose the money. The gap is the
        // whole point of recording both figures.
        Assert.Equal(60m, after!.AmountOutstanding);
        Assert.Equal(SalesOrderStatus.Delivered, after.Status);
    }

    [Fact]
    public async Task Collecting_more_than_was_owed_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));
        await orders.ConfirmAsync(created.Value);
        await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Steadfast",
        });

        var delivered = await orders.MarkDeliveredAsync(new DeliveryRequest
        {
            OrderId = created.Value,
            AmountCollected = 999_999m,
        });

        Assert.False(delivered.Succeeded);
    }

    [Fact]
    public async Task A_returned_parcel_puts_stock_back_in_the_batches_it_left_from()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider, quantity: 8m);

        var created = await orders.CreateAsync(Request(customerId, variantId, 3m));
        await orders.ConfirmAsync(created.Value);
        await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "RedX",
        });

        Assert.Equal(5m, await OnHandAsync(db, variantId));

        var returned = await orders.MarkReturnedAsync(
            created.Value, "Customer refused at the door.");

        Assert.True(returned.Succeeded, returned.Error);

        // Back where it started.
        Assert.Equal(8m, await OnHandAsync(db, variantId));

        var entries = await db.StockLedger
            .Where(e => e.DocumentType == StockDocumentType.SalesOrder
                        && e.DocumentId == created.Value)
            .ToListAsync();

        // The issue and its mirror. The ledger is append-only, so a return is a
        // second entry rather than an edit of the first.
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.MovementType == StockMovementType.Issue);
        Assert.Contains(entries, e => e.MovementType == StockMovementType.ReturnFromCustomer);
        Assert.Equal(0m, entries.Sum(e => e.QuantityChange));

        var order = await orders.GetAsync(created.Value);

        // Nothing was ultimately sold, so it contributed no cost and no margin.
        Assert.Equal(0m, order!.CostOfGoods);
        Assert.Equal(SalesOrderStatus.Returned, order.Status);
    }

    [Fact]
    public async Task A_return_needs_a_reason()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));
        await orders.ConfirmAsync(created.Value);
        await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Steadfast",
        });

        var returned = await orders.MarkReturnedAsync(created.Value, "  ");

        Assert.False(returned.Succeeded);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_order_gives_the_stock_back()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider, quantity: 6m);

        var created = await orders.CreateAsync(Request(customerId, variantId, 4m));
        await orders.ConfirmAsync(created.Value);

        Assert.Equal(4m, await ReservedAsync(db, variantId));

        var cancelled = await orders.CancelAsync(created.Value, "Customer changed their mind.");

        Assert.True(cancelled.Succeeded, cancelled.Error);

        // Released, and no ledger entry either way - nothing ever moved.
        Assert.Equal(0m, await ReservedAsync(db, variantId));
        Assert.Equal(6m, await OnHandAsync(db, variantId));
        Assert.False(await db.StockLedger.AnyAsync(
            e => e.DocumentType == StockDocumentType.SalesOrder && e.DocumentId == created.Value));
    }

    [Fact]
    public async Task A_dispatched_order_cannot_be_cancelled()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));
        await orders.ConfirmAsync(created.Value);
        await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Steadfast",
        });

        var cancelled = await orders.CancelAsync(created.Value, "Changed my mind.");

        // Cancelling would leave stock that has physically gone still counted
        // as being here.
        Assert.False(cancelled.Succeeded);
        Assert.Contains("returned", cancelled.Error!);
    }

    // -----------------------------------------------------------------------
    // Availability
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Available_is_what_is_left_after_other_orders_have_claimed_theirs()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var reservations = scope.ServiceProvider.GetRequiredService<StockReservationService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider, quantity: 10m);

        Assert.Equal(10m, await reservations.AvailableAsync(variantId, _warehouseId));

        var created = await orders.CreateAsync(Request(customerId, variantId, 6m));
        await orders.ConfirmAsync(created.Value);

        // On hand is still ten. Available is not, and available is the only
        // number a salesperson should be shown.
        Assert.Equal(4m, await reservations.AvailableAsync(variantId, _warehouseId));
    }

    [Fact]
    public async Task The_status_history_records_every_move()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var created = await orders.CreateAsync(Request(customerId, variantId));

        await orders.ConfirmAsync(created.Value, "Confirmed on the phone.");
        await orders.MarkPackedAsync(created.Value);
        await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = "Steadfast",
        });
        await orders.MarkDeliveredAsync(new DeliveryRequest { OrderId = created.Value });

        var order = await orders.GetAsync(created.Value);

        Assert.Equal(4, order!.History.Count);
        Assert.Equal(SalesOrderStatus.Confirmed, order.History[0].ToStatus);
        Assert.Equal("Confirmed on the phone.", order.History[0].Note);
        Assert.Equal(SalesOrderStatus.Delivered, order.History[^1].ToStatus);
    }

    [Fact]
    public async Task Numbers_are_sequential_within_a_month()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(scope.ServiceProvider);
        var (variantId, _) = await StockedProductAsync(scope.ServiceProvider);

        var first = await orders.CreateAsync(Request(customerId, variantId));
        var second = await orders.CreateAsync(Request(customerId, variantId));

        var a = (await orders.GetAsync(first.Value))!.Number;
        var b = (await orders.GetAsync(second.Value))!.Number;

        Assert.StartsWith("SO-2609-", a);
        Assert.True(string.CompareOrdinal(b, a) > 0);
    }
}
