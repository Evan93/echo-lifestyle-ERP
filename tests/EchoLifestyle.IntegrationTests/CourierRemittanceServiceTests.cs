using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Finance;
using EchoLifestyle.Application.Finance.Remittances;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// A courier settling up.
///
/// One transfer covering many parcels, net of their fee, against a statement of
/// consignment numbers. The tests that matter are about the three things that go
/// wrong: the arithmetic not agreeing with the money, a parcel coming back
/// instead of being paid for, and the courier's fee vanishing into the netting
/// rather than being recorded as the cost it is.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CourierRemittanceServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public CourierRemittanceServiceTests(DatabaseFixture fixture)
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
        "019" + Random.Shared.Next(10_000_000, 99_999_999).ToString("D8");

    /// <summary>A courier name nobody else in the suite will use.</summary>
    private static string UniqueCourier() => $"Courier-{InventoryTestData.Unique()}";

    private async Task<long> CustomerAsync(IServiceProvider services)
    {
        var customers = services.GetRequiredService<CustomerAdminService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var created = await customers.CreateAsync(new SaveCustomerRequest
        {
            FullName = $"Remit customer {InventoryTestData.Unique()}",
            Phone = UniquePhone(),
            Source = CustomerSource.Messenger,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        var districtId = await db.Districts.Where(d => d.Name == "Dhaka").Select(d => d.Id).FirstAsync();

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = districtId,
            AreaOrThana = "Uttara",
            AddressLine = "House 9, Road 4",
        });

        return created.Value;
    }

    /// <summary>An order dispatched with the given courier, ready to be settled.</summary>
    private async Task<(long OrderId, decimal Total, long VariantId)> DispatchedOrderAsync(
        IServiceProvider services,
        string courier,
        decimal quantity = 2m,
        decimal unitPrice = 500m,
        decimal stock = 10m)
    {
        var orders = services.GetRequiredService<SalesOrderService>();

        var customerId = await CustomerAsync(services);

        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(services, suffix);
        var variant = await InventoryTestData.VariantAsync(services, suffix);

        await InventoryTestData.ReceiveAsync(
            services, supplierId, _warehouseId, _branchId, variant.VariantId, stock, 200m);

        var created = await orders.CreateAsync(new SaveOrderRequest
        {
            CustomerId = customerId,
            BranchId = _branchId,
            WarehouseId = _warehouseId,
            OrderDate = new DateOnly(2026, 9, 7),
            DeliveryCharge = 0m,
            Lines =
            [
                new OrderLineInput
                {
                    ProductVariantId = variant.VariantId,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                },
            ],
        });

        Assert.True(created.Succeeded, created.Error);
        Assert.True((await orders.ConfirmAsync(created.Value)).Succeeded);

        var dispatched = await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = created.Value,
            CourierName = courier,
            ConsignmentNumber = $"CN-{InventoryTestData.Unique()}",
        });

        Assert.True(dispatched.Succeeded, dispatched.Error);

        var order = await orders.GetAsync(created.Value);

        return (created.Value, order!.GrandTotal, variant.VariantId);
    }

    private async Task<long> StartAsync(IServiceProvider services, string courier)
    {
        var remittances = services.GetRequiredService<CourierRemittanceService>();

        var started = await remittances.StartAsync(new StartRemittanceRequest
        {
            CourierName = courier,
            BranchId = _branchId,
            RemittanceDate = new DateOnly(2026, 9, 7),
            StatementReference = $"ST-{InventoryTestData.Unique()}",
            ReceivedVia = PaymentMethodKind.Bkash,
        });

        Assert.True(started.Succeeded, started.Error);

        return started.Value;
    }

    // -----------------------------------------------------------------------
    // Finding what needs settling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_courier_holding_parcels_shows_what_it_owes()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var first = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var second = await DispatchedOrderAsync(scope.ServiceProvider, courier);

        var unsettled = await remittances.GetUnsettledAsync(courier);

        Assert.Equal(2, unsettled.Count);
        Assert.Equal(first.Total + second.Total, unsettled.Sum(u => u.Outstanding));

        var exposure = await remittances.GetExposureAsync();
        var mine = Assert.Single(exposure.Where(e => e.CourierName == courier));

        Assert.Equal(2, mine.Parcels);
        Assert.Equal(first.Total + second.Total, mine.Outstanding);
    }

    [Fact]
    public async Task An_order_already_on_an_open_statement_is_not_offered_twice()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = order.Total,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = order.Total,
            }],
        });

        // Listing it again would invite settling it twice, and the totals would
        // still add up.
        Assert.Empty(await remittances.GetUnsettledAsync(courier));
    }

    // -----------------------------------------------------------------------
    // Posting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Posting_settles_the_orders_and_records_the_courier_fee_as_money_out()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var courier = UniqueCourier();
        var first = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var second = await DispatchedOrderAsync(scope.ServiceProvider, courier);

        var gross = first.Total + second.Total;
        var fee = 120m;

        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var saved = await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            CourierFee = fee,
            NetReceived = gross - fee,
            Lines =
            [
                new RemittanceLineInput
                {
                    SalesOrderId = first.OrderId,
                    AmountCollected = first.Total,
                },
                new RemittanceLineInput
                {
                    SalesOrderId = second.OrderId,
                    AmountCollected = second.Total,
                },
            ],
        });

        Assert.True(saved.Succeeded, saved.Error);

        var posted = await remittances.PostAsync(remittanceId);

        Assert.True(posted.Succeeded, posted.Error);
        Assert.Equal(2, posted.Value!.Settled);

        // Both orders are delivered and paid.
        foreach (var id in new[] { first.OrderId, second.OrderId })
        {
            var order = await orders.GetAsync(id);

            Assert.Equal(SalesOrderStatus.Delivered, order!.Status);
            Assert.Equal(0m, order.AmountOutstanding);
        }

        // The fee is money out, not a netting-off. Hidden inside the receipt it
        // would be absent from every margin figure in the system.
        var feeEntry = await db.CashTransactions
            .SingleAsync(t => t.CourierRemittanceId == remittanceId
                              && t.Kind == CashKind.CourierFee);

        Assert.Equal(CashDirection.Out, feeEntry.Direction);
        Assert.Equal(fee, feeEntry.Amount);

        // And the money in matches the orders.
        var collected = await db.CashTransactions
            .Where(t => t.CourierRemittanceId == remittanceId
                        && t.Kind == CashKind.OrderCollection)
            .SumAsync(t => t.Amount);

        Assert.Equal(gross, collected);
    }

    [Fact]
    public async Task A_statement_that_does_not_add_up_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            CourierFee = 50m,

            // 100 less than the arithmetic says should have arrived.
            NetReceived = order.Total - 50m - 100m,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = order.Total,
            }],
        });

        var posted = await remittances.PostAsync(remittanceId);

        // Silently accepting this is how a missing transfer goes unnoticed for
        // a quarter.
        Assert.False(posted.Succeeded);
        Assert.Contains("does not add up", posted.Error!);
    }

    [Fact]
    public async Task A_discrepancy_can_be_accepted_deliberately()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = order.Total - 75m,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = order.Total,
            }],
        });

        var posted = await remittances.PostAsync(remittanceId, acceptDiscrepancy: true);

        Assert.True(posted.Succeeded, posted.Error);

        // Accepted, but recorded - the audit trail says who decided that.
        var entry = await db.AuditLog
            .Where(a => a.Action == "Finance.Remittance.Posted"
                        && a.EntityId == remittanceId.ToString())
            .FirstAsync();

        Assert.Contains("discrepancy", entry.Summary!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_short_collection_leaves_the_rest_outstanding()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var collected = order.Total - 200m;

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = collected,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = collected,
                Notes = "Customer haggled at the door.",
            }],
        });

        Assert.True((await remittances.PostAsync(remittanceId)).Succeeded);

        var settled = await orders.GetAsync(order.OrderId);

        // Delivered, but still owing. A shortfall is a debt, not a failed
        // delivery.
        Assert.Equal(SalesOrderStatus.Delivered, settled!.Status);
        Assert.Equal(200m, settled.AmountOutstanding);
    }

    [Fact]
    public async Task A_returned_parcel_on_the_statement_goes_back_on_the_shelf()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(
            scope.ServiceProvider, courier, quantity: 3m, stock: 10m);

        // Three units left when it shipped.
        var afterDispatch = await db.StockBalances
            .Where(b => b.ProductVariantId == order.VariantId && b.WarehouseId == _warehouseId)
            .SumAsync(b => b.QuantityOnHand);

        Assert.Equal(7m, afterDispatch);

        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,

            // Nothing collected, nothing received - the parcel came back.
            NetReceived = 0m,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                IsReturned = true,
                ReturnReason = "Customer unreachable after three attempts.",
            }],
        });

        var posted = await remittances.PostAsync(remittanceId);

        Assert.True(posted.Succeeded, posted.Error);
        Assert.Equal(1, posted.Value!.Returned);
        Assert.Equal(0, posted.Value.Settled);

        var settled = await orders.GetAsync(order.OrderId);
        Assert.Equal(SalesOrderStatus.Returned, settled!.Status);

        // Back where it started, in the batches it left from.
        var afterReturn = await db.StockBalances
            .Where(b => b.ProductVariantId == order.VariantId && b.WarehouseId == _warehouseId)
            .SumAsync(b => b.QuantityOnHand);

        Assert.Equal(10m, afterReturn);

        Assert.True(await db.StockLedger.AnyAsync(
            e => e.DocumentType == StockDocumentType.SalesOrder
                 && e.DocumentId == order.OrderId
                 && e.MovementType == StockMovementType.ReturnFromCustomer));
    }

    [Fact]
    public async Task A_returned_line_needs_a_reason()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var saved = await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                IsReturned = true,
            }],
        });

        Assert.False(saved.Succeeded);
        Assert.Contains("why", saved.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Collecting_more_than_is_owed_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var saved = await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = order.Total + 500m,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = order.Total + 500m,
            }],
        });

        Assert.False(saved.Succeeded);
        Assert.Contains("outstanding", saved.Error!);
    }

    [Fact]
    public async Task The_same_order_twice_on_one_statement_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var saved = await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            Lines =
            [
                new RemittanceLineInput { SalesOrderId = order.OrderId, AmountCollected = 100m },
                new RemittanceLineInput { SalesOrderId = order.OrderId, AmountCollected = 200m },
            ],
        });

        Assert.False(saved.Succeeded);
        Assert.Contains("twice", saved.Error!);
    }

    [Fact]
    public async Task An_order_that_never_shipped_cannot_be_settled()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();

        var courier = UniqueCourier();
        var customerId = await CustomerAsync(scope.ServiceProvider);
        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(scope.ServiceProvider, suffix);
        var variant = await InventoryTestData.VariantAsync(scope.ServiceProvider, suffix);

        await InventoryTestData.ReceiveAsync(
            scope.ServiceProvider, supplierId, _warehouseId, _branchId, variant.VariantId, 5m, 100m);

        var draft = await orders.CreateAsync(new SaveOrderRequest
        {
            CustomerId = customerId,
            BranchId = _branchId,
            WarehouseId = _warehouseId,
            OrderDate = new DateOnly(2026, 9, 7),
            Lines = [new OrderLineInput
            {
                ProductVariantId = variant.VariantId,
                Quantity = 1m,
                UnitPrice = 300m,
            }],
        });

        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var saved = await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = 300m,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = draft.Value,
                AmountCollected = 300m,
            }],
        });

        Assert.False(saved.Succeeded);
        Assert.Contains("not with a courier", saved.Error!);
    }

    [Fact]
    public async Task Posting_twice_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = order.Total,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = order.Total,
            }],
        });

        Assert.True((await remittances.PostAsync(remittanceId)).Succeeded);

        var again = await remittances.PostAsync(remittanceId);

        Assert.False(again.Succeeded);
        Assert.Contains("already posted", again.Error!);
    }

    // -----------------------------------------------------------------------
    // The money log itself
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Marking_one_order_delivered_writes_a_cash_transaction()
    {
        await using var scope = _fixture.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashTransactionWriter>();

        var order = await DispatchedOrderAsync(scope.ServiceProvider, UniqueCourier());

        var delivered = await orders.MarkDeliveredAsync(new DeliveryRequest
        {
            OrderId = order.OrderId,
        });

        Assert.True(delivered.Succeeded, delivered.Error);

        // The order's collected figure is a projection of the log, not a number
        // somebody typed over the top of it.
        var fromLog = await cash.CollectedForOrderAsync(order.OrderId);
        var settled = await orders.GetAsync(order.OrderId);

        Assert.Equal(order.Total, fromLog);
        Assert.Equal(fromLog, settled!.AmountCollected);
    }

    [Fact]
    public async Task The_collected_column_always_agrees_with_the_log()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();
        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var cash = scope.ServiceProvider.GetRequiredService<CashTransactionWriter>();

        var courier = UniqueCourier();
        var order = await DispatchedOrderAsync(scope.ServiceProvider, courier);
        var remittanceId = await StartAsync(scope.ServiceProvider, courier);

        var part = order.Total - 150m;

        await remittances.SaveAsync(new SaveRemittanceRequest
        {
            Id = remittanceId,
            NetReceived = part,
            Lines = [new RemittanceLineInput
            {
                SalesOrderId = order.OrderId,
                AmountCollected = part,
            }],
        });

        Assert.True((await remittances.PostAsync(remittanceId)).Succeeded);

        var settled = await orders.GetAsync(order.OrderId);
        var fromLog = await cash.CollectedForOrderAsync(order.OrderId);

        Assert.Equal(part, fromLog);
        Assert.Equal(fromLog, settled!.AmountCollected);
    }

    [Fact]
    public async Task Numbers_are_sequential_within_a_month()
    {
        await using var scope = _fixture.CreateScope();
        var remittances = scope.ServiceProvider.GetRequiredService<CourierRemittanceService>();

        var first = await StartAsync(scope.ServiceProvider, UniqueCourier());
        var second = await StartAsync(scope.ServiceProvider, UniqueCourier());

        var a = (await remittances.GetAsync(first))!.Number;
        var b = (await remittances.GetAsync(second))!.Number;

        Assert.StartsWith("REM-2609-", a);
        Assert.True(string.CompareOrdinal(b, a) > 0);
    }
}
