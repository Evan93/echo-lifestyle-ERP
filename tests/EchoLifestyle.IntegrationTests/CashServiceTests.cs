using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Finance;
using EchoLifestyle.Application.Finance.Cash;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Money out, and the log that has to survive it.
///
/// The tests here are about the four ways a cash log stops being trustworthy: an
/// entry pointing the wrong way, a cost recorded twice, a correction made by
/// editing rather than reversing, and a refund of money that never arrived.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CashServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public CashServiceTests(DatabaseFixture fixture)
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

    // -----------------------------------------------------------------------
    // Setup helpers
    // -----------------------------------------------------------------------

    private static string UniquePhone() =>
        "019" + Random.Shared.Next(10_000_000, 99_999_999).ToString("D8");

    /// <summary>A category nobody else in the suite is spending against.</summary>
    private static async Task<long> CategoryAsync(
        IServiceProvider services,
        bool costOfSale = false)
    {
        var categories = services.GetRequiredService<ExpenseCategoryService>();

        var saved = await categories.SaveAsync(new SaveExpenseCategoryRequest
        {
            Name = $"Category {InventoryTestData.Unique()}",
            IsCostOfSale = costOfSale,
            IsActive = true,
        });

        Assert.True(saved.Succeeded, saved.Error);

        return saved.Value;
    }

    private static async Task<long> PartnerAsync(IServiceProvider services)
    {
        var partners = services.GetRequiredService<PartnerService>();

        var saved = await partners.SaveAsync(new SavePartnerRequest
        {
            Name = $"Partner {InventoryTestData.Unique()}",
            IsActive = true,
        });

        Assert.True(saved.Succeeded, saved.Error);

        return saved.Value;
    }

    private async Task<(long OrderId, string Number, decimal Total)> DispatchedOrderAsync(
        IServiceProvider services)
    {
        var orders = services.GetRequiredService<SalesOrderService>();
        var customers = services.GetRequiredService<CustomerAdminService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var created = await customers.CreateAsync(new SaveCustomerRequest
        {
            FullName = $"Cash customer {InventoryTestData.Unique()}",
            Phone = UniquePhone(),
            Source = CustomerSource.Messenger,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        var districtId = await db.Districts
            .Where(d => d.Name == "Dhaka")
            .Select(d => d.Id)
            .FirstAsync();

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = districtId,
            AreaOrThana = "Uttara",
            AddressLine = "House 9, Road 4",
        });

        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(services, suffix);
        var variant = await InventoryTestData.VariantAsync(services, suffix);

        await InventoryTestData.ReceiveAsync(
            services, supplierId, _warehouseId, _branchId, variant.VariantId, 10m, 200m);

        var order = await orders.CreateAsync(new SaveOrderRequest
        {
            CustomerId = created.Value,
            BranchId = _branchId,
            WarehouseId = _warehouseId,
            OrderDate = new DateOnly(2026, 9, 7),
            DeliveryCharge = 0m,
            Lines =
            [
                new OrderLineInput
                {
                    ProductVariantId = variant.VariantId,
                    Quantity = 2m,
                    UnitPrice = 500m,
                },
            ],
        });

        Assert.True(order.Succeeded, order.Error);
        Assert.True((await orders.ConfirmAsync(order.Value)).Succeeded);

        var dispatched = await orders.DispatchAsync(new DispatchRequest
        {
            OrderId = order.Value,
            CourierName = $"Courier-{InventoryTestData.Unique()}",
            ConsignmentNumber = $"CN-{InventoryTestData.Unique()}",
        });

        Assert.True(dispatched.Succeeded, dispatched.Error);

        var detail = await orders.GetAsync(order.Value);

        return (order.Value, detail!.Number, detail.GrandTotal);
    }

    private RecordCashRequest Expense(long categoryId, decimal amount) => new()
    {
        Kind = CashKind.Expense,
        Method = PaymentMethodKind.Cash,
        Amount = amount,
        BranchId = _branchId,
        ExpenseCategoryId = categoryId,
        TransactionDate = new DateOnly(2026, 9, 7),
    };

    // -----------------------------------------------------------------------
    // Direction is not a question anybody gets asked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_expense_is_money_out_without_anybody_choosing_that()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);

        var recorded = await cash.RecordAsync(Expense(categoryId, 1_250m));
        Assert.True(recorded.Succeeded, recorded.Error);

        var entry = await db.CashTransactions.AsNoTracking()
            .FirstAsync(t => t.Id == recorded.Value);

        // The request carries no direction at all. Asking would only create the
        // chance of an expense that increased the cash balance.
        Assert.Equal(CashDirection.Out, entry.Direction);
        Assert.Equal(-1_250m, entry.SignedAmount);
    }

    [Fact]
    public async Task Partner_capital_is_money_in()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var partnerId = await PartnerAsync(scope.ServiceProvider);

        var recorded = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.PartnerCapital,
            Method = PaymentMethodKind.Bkash,
            Amount = 50_000m,
            BranchId = _branchId,
            PartnerId = partnerId,
        });

        Assert.True(recorded.Succeeded, recorded.Error);

        var entry = await db.CashTransactions.AsNoTracking()
            .FirstAsync(t => t.Id == recorded.Value);

        Assert.Equal(CashDirection.In, entry.Direction);
    }

    // -----------------------------------------------------------------------
    // What the form has to insist on
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_expense_with_no_category_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var result = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.Expense,
            Method = PaymentMethodKind.Cash,
            Amount = 400m,
            BranchId = _branchId,
        });

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(RecordCashRequest.ExpenseCategoryId), result.Field);
    }

    [Fact]
    public async Task Capital_with_no_partner_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var result = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.PartnerCapital,
            Method = PaymentMethodKind.Cash,
            Amount = 10_000m,
            BranchId = _branchId,
        });

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(RecordCashRequest.PartnerId), result.Field);
    }

    /// <summary>
    /// A courier's fee is written when the payout posts. Typing one here would
    /// put the cost of delivery in twice, and both entries would look right.
    /// </summary>
    [Fact]
    public async Task A_courier_charge_cannot_be_typed_by_hand()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var result = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.CourierFee,
            Method = PaymentMethodKind.Bkash,
            Amount = 60m,
            BranchId = _branchId,
        });

        Assert.False(result.Succeeded);
        Assert.Contains("twice", result.Error);
    }

    [Fact]
    public async Task Money_cannot_have_moved_in_the_future()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);

        var request = Expense(categoryId, 100m);
        request.TransactionDate = new DateOnly(2030, 1, 1);

        var result = await cash.RecordAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(RecordCashRequest.TransactionDate), result.Field);
    }

    // -----------------------------------------------------------------------
    // Corrections
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reversing_keeps_both_entries_and_nets_the_category_to_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);

        // Ten times what it should have been - an extra zero, which is how this
        // mistake is actually made.
        var recorded = await cash.RecordAsync(Expense(categoryId, 12_000m));
        Assert.True(recorded.Succeeded, recorded.Error);

        var reversed = await cash.ReverseAsync(recorded.Value, "Typed an extra zero.");
        Assert.True(reversed.Succeeded, reversed.Error);

        var entries = await db.CashTransactions.AsNoTracking()
            .Where(t => t.ExpenseCategoryId == categoryId)
            .ToListAsync();

        // Both survive. An append-only log that quietly loses the mistake is
        // worth no more than a column somebody typed over.
        Assert.Equal(2, entries.Count);
        Assert.Equal(0m, entries.Sum(e => e.SignedAmount));

        var reversal = entries.Single(e => e.ReversesCashTransactionId is not null);

        // The reversal keeps the original's kind and category, which is what
        // lets the breakdown net without knowing reversals exist.
        Assert.Equal(CashKind.Expense, reversal.Kind);
        Assert.Equal(categoryId, reversal.ExpenseCategoryId);

        var breakdown = await cash.GetExpenseBreakdownAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var mine = breakdown.Rows.SingleOrDefault(r => r.ExpenseCategoryId == categoryId);

        Assert.NotNull(mine);
        Assert.Equal(0m, mine!.Amount);
    }

    [Fact]
    public async Task An_entry_cannot_be_reversed_twice()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);
        var recorded = await cash.RecordAsync(Expense(categoryId, 800m));

        Assert.True((await cash.ReverseAsync(recorded.Value, "Wrong category.")).Succeeded);

        // Reversing twice halves a figure that was only ever wrong once, and
        // the log still looks internally consistent.
        var second = await cash.ReverseAsync(recorded.Value, "Again.");

        Assert.False(second.Succeeded);
        Assert.Contains("already been reversed", second.Error);
    }

    [Fact]
    public async Task A_reversal_cannot_itself_be_reversed()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);
        var recorded = await cash.RecordAsync(Expense(categoryId, 300m));
        var reversal = await cash.ReverseAsync(recorded.Value, "Not ours.");

        var result = await cash.ReverseAsync(reversal.Value, "Actually it was.");

        Assert.False(result.Succeeded);
        Assert.Contains("record the entry again", result.Error);
    }

    [Fact]
    public async Task A_reversal_needs_a_reason()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);
        var recorded = await cash.RecordAsync(Expense(categoryId, 300m));

        var result = await cash.ReverseAsync(recorded.Value, "   ");

        Assert.False(result.Succeeded);
    }

    // -----------------------------------------------------------------------
    // Money against an order
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_direct_payment_and_its_refund_move_what_the_order_says_it_collected()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var writer = scope.ServiceProvider.GetRequiredService<CashTransactionWriter>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var order = await DispatchedOrderAsync(scope.ServiceProvider);

        var collected = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.OrderCollection,
            Method = PaymentMethodKind.Bkash,
            Amount = order.Total,
            BranchId = _branchId,
            OrderNumber = order.Number,
        });

        Assert.True(collected.Succeeded, collected.Error);

        Assert.Equal(
            order.Total,
            await db.SalesOrders.AsNoTracking()
                .Where(o => o.Id == order.OrderId)
                .Select(o => o.AmountCollected)
                .FirstAsync());

        var refunded = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.CustomerRefund,
            Method = PaymentMethodKind.Bkash,
            Amount = 200m,
            BranchId = _branchId,
            OrderNumber = order.Number,
        });

        Assert.True(refunded.Succeeded, refunded.Error);

        // The projection and the log agree, because the writer moves both in
        // the same call.
        Assert.Equal(
            order.Total - 200m,
            await db.SalesOrders.AsNoTracking()
                .Where(o => o.Id == order.OrderId)
                .Select(o => o.AmountCollected)
                .FirstAsync());

        Assert.Equal(order.Total - 200m, await writer.CollectedForOrderAsync(order.OrderId));
    }

    [Fact]
    public async Task Refunding_more_than_was_collected_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var order = await DispatchedOrderAsync(scope.ServiceProvider);

        await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.OrderCollection,
            Method = PaymentMethodKind.Cash,
            Amount = 100m,
            BranchId = _branchId,
            OrderNumber = order.Number,
        });

        var result = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.CustomerRefund,
            Method = PaymentMethodKind.Cash,
            Amount = 500m,
            BranchId = _branchId,
            OrderNumber = order.Number,
        });

        // Giving back more than came in is a payment, and it should be recorded
        // as one rather than hidden inside a refund.
        Assert.False(result.Succeeded);
        Assert.Equal(nameof(RecordCashRequest.Amount), result.Field);
    }

    [Fact]
    public async Task Collecting_more_than_the_order_is_worth_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var order = await DispatchedOrderAsync(scope.ServiceProvider);

        var result = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.OrderCollection,
            Method = PaymentMethodKind.Cash,
            Amount = order.Total + 1m,
            BranchId = _branchId,
            OrderNumber = order.Number,
        });

        Assert.False(result.Succeeded);
        Assert.Contains("outstanding", result.Error);
    }

    // -----------------------------------------------------------------------
    // The summaries
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_position_nets_each_method_separately()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);
        var partnerId = await PartnerAsync(scope.ServiceProvider);

        var period = new DateOnly(2026, 9, 7);

        await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.PartnerCapital,
            Method = PaymentMethodKind.Bkash,
            Amount = 20_000m,
            BranchId = _branchId,
            PartnerId = partnerId,
            TransactionDate = period,
        });

        var spend = Expense(categoryId, 3_000m);
        spend.Method = PaymentMethodKind.Bkash;
        await cash.RecordAsync(spend);

        var position = await cash.GetPositionAsync(period, period);
        var bkash = position.Rows.Single(r => r.Method == PaymentMethodKind.Bkash);

        // Other tests share this database, so the assertion is on the movement
        // this test caused rather than on an absolute balance.
        Assert.True(bkash.In >= 20_000m);
        Assert.True(bkash.Out >= 3_000m);
        Assert.Equal(bkash.Opening + bkash.In - bkash.Out, bkash.Closing);
    }

    [Fact]
    public async Task Costs_that_rise_with_sales_are_reported_apart_from_the_rest()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var packaging = await CategoryAsync(scope.ServiceProvider, costOfSale: true);
        var rent = await CategoryAsync(scope.ServiceProvider);

        await cash.RecordAsync(Expense(packaging, 1_500m));
        await cash.RecordAsync(Expense(rent, 9_000m));

        var breakdown = await cash.GetExpenseBreakdownAsync(
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7));

        var packagingRow = breakdown.Rows.Single(r => r.ExpenseCategoryId == packaging);
        var rentRow = breakdown.Rows.Single(r => r.ExpenseCategoryId == rent);

        Assert.True(packagingRow.IsCostOfSale);
        Assert.False(rentRow.IsCostOfSale);
        Assert.Equal(1_500m, packagingRow.Amount);
        Assert.Equal(9_000m, rentRow.Amount);

        // Keeping them apart is what makes "are we making money on this
        // product?" a different question from "are we making money?".
        Assert.True(breakdown.CostOfSale >= 1_500m);
        Assert.Equal(breakdown.Total - breakdown.CostOfSale, breakdown.Operating);
    }

    [Fact]
    public async Task A_partners_account_shows_what_they_put_in_and_took_out()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var partnerId = await PartnerAsync(scope.ServiceProvider);

        await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.PartnerCapital,
            Method = PaymentMethodKind.Bkash,
            Amount = 75_000m,
            BranchId = _branchId,
            PartnerId = partnerId,
        });

        await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.PartnerDrawing,
            Method = PaymentMethodKind.Cash,
            Amount = 5_000m,
            BranchId = _branchId,
            PartnerId = partnerId,
        });

        var ledger = await cash.GetPartnerLedgerAsync();
        var mine = ledger.Single(p => p.PartnerId == partnerId);

        Assert.Equal(75_000m, mine.CapitalIn);
        Assert.Equal(5_000m, mine.Drawings);
        Assert.Equal(70_000m, mine.Balance);
    }

    [Fact]
    public async Task A_reversed_contribution_reduces_capital_rather_than_looking_like_a_drawing()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var partnerId = await PartnerAsync(scope.ServiceProvider);

        var recorded = await cash.RecordAsync(new RecordCashRequest
        {
            Kind = CashKind.PartnerCapital,
            Method = PaymentMethodKind.Bkash,
            Amount = 30_000m,
            BranchId = _branchId,
            PartnerId = partnerId,
        });

        Assert.True((await cash.ReverseAsync(recorded.Value, "It was the other partner.")).Succeeded);

        var mine = (await cash.GetPartnerLedgerAsync()).Single(p => p.PartnerId == partnerId);

        Assert.Equal(0m, mine.CapitalIn);
        Assert.Equal(0m, mine.Drawings);
    }

    // -----------------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_built_in_category_cannot_be_renamed()
    {
        await using var scope = _fixture.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var packaging = (await categories.ListAsync()).First(c => c.IsSystem);

        var result = await categories.SaveAsync(new SaveExpenseCategoryRequest
        {
            Id = packaging.Id,
            Name = $"Renamed {InventoryTestData.Unique()}",
            IsActive = true,
        });

        // The next startup would recreate the original name, and a year of
        // spending would sit under two headings meaning the same thing.
        Assert.False(result.Succeeded);
        Assert.Contains("built-in", result.Error);
    }

    [Fact]
    public async Task Two_categories_cannot_share_a_name()
    {
        await using var scope = _fixture.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var name = $"Category {InventoryTestData.Unique()}";

        Assert.True((await categories.SaveAsync(new SaveExpenseCategoryRequest { Name = name }))
            .Succeeded);

        var second = await categories.SaveAsync(new SaveExpenseCategoryRequest { Name = name });

        Assert.False(second.Succeeded);
        Assert.Equal(nameof(SaveExpenseCategoryRequest.Name), second.Field);
    }

    [Fact]
    public async Task An_inactive_category_cannot_be_spent_against()
    {
        await using var scope = _fixture.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var categories = scope.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var categoryId = await CategoryAsync(scope.ServiceProvider);

        var retired = await categories.SaveAsync(new SaveExpenseCategoryRequest
        {
            Id = categoryId,
            Name = $"Retired {InventoryTestData.Unique()}",
            IsActive = false,
        });

        Assert.True(retired.Succeeded, retired.Error);

        var result = await cash.RecordAsync(Expense(categoryId, 100m));

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(RecordCashRequest.ExpenseCategoryId), result.Field);
    }
}
