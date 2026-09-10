using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Reporting;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// What the numbers are allowed to say.
///
/// Almost everything worth testing here is a definition rather than a
/// calculation. Summing is easy; agreeing on what counts as a sale is the part
/// that decides whether the owner is looking at revenue or at wishes.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SalesReportServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    /// <summary>
    /// A date no other test writes an order on.
    ///
    /// These tests assert exact counts over a date range, and the range does
    /// not know whose orders it is counting. Every other suite works on
    /// "today", so anchoring here to a fixed day years in the past is what
    /// stops this class's arithmetic being broken by an unrelated test that
    /// happened to run first.
    /// </summary>
    private static readonly DateOnly ReportDate = new(2019, 6, 15);

    private long _branchId;
    private long _warehouseId;

    public SalesReportServiceTests(DatabaseFixture fixture)
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

    /// <summary>
    /// Writes an order straight to the database at a chosen status and date.
    ///
    /// Deliberately not driven through the order lifecycle: these tests are
    /// about how a report reads finished orders, and walking every one of them
    /// through confirm, pack and dispatch would test the lifecycle again while
    /// making the arrangement unreadable. The lifecycle has its own tests.
    /// </summary>
    private async Task<SalesOrder> OrderAsync(
        IServiceProvider services,
        DateOnly date,
        SalesOrderStatus status,
        decimal subTotal = 1000m,
        decimal cost = 600m,
        decimal collected = 0m,
        int daysOut = 0)
    {
        var db = services.GetRequiredService<EchoDbContext>();
        var customerId = await CustomerAsync(services);

        var order = new SalesOrder
        {
            Number = "RPT-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            CustomerId = customerId,
            BranchId = _branchId,
            WarehouseId = _warehouseId,
            OrderDate = date,
            Status = status,
            RecipientName = "Report test",
            RecipientPhone = "01811111111",
            DivisionName = "Dhaka",
            DistrictName = "Dhaka",
            AreaOrThana = "Uttara",
            AddressLine = "House 1",
            SubTotal = subTotal,
            DiscountAmount = 0m,
            DeliveryCharge = 60m,
            GrandTotal = subTotal + 60m,
            AmountCollected = collected,
            CostOfGoods = cost,
            CourierName = "Steadfast",
            ConsignmentNumber = "CN" + Random.Shared.Next(100000, 999999),
            DispatchedAtUtc = status is SalesOrderStatus.Dispatched or SalesOrderStatus.Delivered
                ? DateTime.UtcNow.AddDays(-daysOut)
                : null,
        };

        order.Lines.Add(new SalesOrderLine
        {
            ProductVariantId = await db.ProductVariants.Select(v => v.Id).FirstAsync(),
            Sku = "RPT-SKU",
            ProductName = "Report product",
            VariantName = "Report product",
            Quantity = 2m,
            UnitPrice = subTotal / 2m,
            LineTotal = subTotal,
            CostOfGoods = cost,
        });

        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        return order;
    }

    /// <summary>
    /// A customer to hang the orders on. Created through the real service so
    /// the row satisfies every rule the schema enforces - a hand-built one is
    /// a foreign key waiting to fail.
    /// </summary>
    private static async Task<long> CustomerAsync(IServiceProvider services)
    {
        var customers = services.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(new SaveCustomerRequest
        {
            FullName = $"Report customer {InventoryTestData.Unique()}",
            Phone = "018" + Random.Shared.Next(10_000_000, 99_999_999).ToString("D8"),
            Source = CustomerSource.Messenger,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        return created.Value;
    }

    /// <summary>
    /// Removes this class's own orders, leftovers from a previous run included.
    ///
    /// Children first, and that is not fussiness. <c>ExecuteDeleteAsync</c>
    /// issues a plain SQL DELETE: it does not load the entities, so it does not
    /// cascade the way <c>Remove</c> would, and the order-lines foreign key
    /// refuses the statement outright.
    /// </summary>
    private static async Task ClearAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<EchoDbContext>();

        var orderIds = await db.SalesOrders
            .Where(o => o.Number.StartsWith("RPT-"))
            .Select(o => o.Id)
            .ToListAsync();

        if (orderIds.Count == 0)
        {
            return;
        }

        await db.SalesOrderLines
            .Where(l => orderIds.Contains(l.SalesOrderId))
            .ExecuteDeleteAsync();

        await db.SalesOrders
            .Where(o => orderIds.Contains(o.Id))
            .ExecuteDeleteAsync();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// The definition the whole report rests on. A confirmed order is a promise
    /// somebody can still cancel by telephone; counting it as revenue means
    /// reporting money that has not been earned and may never be.
    /// </summary>
    [Fact]
    public async Task Only_orders_that_reached_the_courier_count_as_sales()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var today = ReportDate;

        await OrderAsync(services, today, SalesOrderStatus.Draft);
        await OrderAsync(services, today, SalesOrderStatus.Confirmed);
        await OrderAsync(services, today, SalesOrderStatus.Packed);
        await OrderAsync(services, today, SalesOrderStatus.Cancelled);
        await OrderAsync(services, today, SalesOrderStatus.Dispatched);
        await OrderAsync(services, today, SalesOrderStatus.Delivered);

        var summary = await reports.GetSummaryAsync(today, today);

        // Two of the six: dispatched and delivered.
        Assert.Equal(2, summary.Orders);
        Assert.Equal(2000m, summary.Sales);
    }

    /// <summary>
    /// A return is not a sale that did not happen. It happened, cost delivery
    /// money twice, and then unhappened - so it comes out of the sales figures
    /// and is reported on its own rather than quietly disappearing.
    /// </summary>
    [Fact]
    public async Task A_return_leaves_the_sales_figure_and_is_reported_separately()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var today = ReportDate;

        await OrderAsync(services, today, SalesOrderStatus.Delivered);
        await OrderAsync(services, today, SalesOrderStatus.Delivered);
        await OrderAsync(services, today, SalesOrderStatus.Returned);

        var summary = await reports.GetSummaryAsync(today, today);

        Assert.Equal(2, summary.Orders);
        Assert.Equal(1, summary.ReturnedOrders);

        // One in three went out and came back.
        Assert.Equal(33.3m, summary.ReturnRatePercent);
    }

    [Fact]
    public async Task Margin_is_sales_less_what_the_goods_cost()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var today = ReportDate;

        await OrderAsync(services, today, SalesOrderStatus.Delivered, subTotal: 1000m, cost: 600m);

        var summary = await reports.GetSummaryAsync(today, today);

        Assert.Equal(400m, summary.Margin);
        Assert.Equal(40m, summary.MarginPercent);

        // Delivery is charged to the customer and is not a sale of goods, so it
        // sits outside the margin it would otherwise flatter.
        Assert.Equal(1000m, summary.Sales);
        Assert.Equal(60m, summary.DeliveryCharged);
    }

    /// <summary>
    /// A gap in a sales table reads as missing data. A zero reads as a quiet
    /// day, which is what it was.
    /// </summary>
    [Fact]
    public async Task Every_day_in_the_range_appears_even_the_empty_ones()
    {
        await using var scope = _fixture.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<SalesReportService>();

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-6);

        var summary = await reports.GetSummaryAsync(from, to);

        Assert.Equal(7, summary.Days.Count);
        Assert.Equal(from, summary.Days[0].Date);
        Assert.Equal(to, summary.Days[^1].Date);
    }

    [Fact]
    public async Task A_backwards_date_range_is_read_the_way_it_was_meant()
    {
        await using var scope = _fixture.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<SalesReportService>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var summary = await reports.GetSummaryAsync(today, today.AddDays(-3));

        Assert.Equal(today.AddDays(-3), summary.From);
        Assert.Equal(today, summary.To);
        Assert.Equal(4, summary.Days.Count);
    }

    // -----------------------------------------------------------------------
    // Money owed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Only_shipped_and_unpaid_parcels_are_outstanding()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var today = ReportDate;

        // Shipped and unpaid: owed.
        await OrderAsync(services, today, SalesOrderStatus.Dispatched, collected: 0m);

        // Shipped and paid in full: not owed.
        await OrderAsync(services, today, SalesOrderStatus.Delivered, collected: 1060m);

        // Never shipped: nothing to be owed for.
        await OrderAsync(services, today, SalesOrderStatus.Confirmed, collected: 0m);

        var outstanding = await reports.GetOutstandingAsync();
        var mine = outstanding.Rows.Where(r => r.Number.StartsWith("RPT-")).ToList();

        Assert.Single(mine);
        Assert.Equal(1060m, mine[0].Outstanding);
    }

    /// <summary>
    /// Aged from dispatch, not from the order date. A parcel that sat in the
    /// shop for a week has not been out with a courier for a week, and chasing
    /// the courier over it would be chasing the wrong party.
    /// </summary>
    [Fact]
    public async Task Age_is_counted_from_the_day_the_parcel_left()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var old = ReportDate;

        await OrderAsync(services, old, SalesOrderStatus.Dispatched, daysOut: 20);

        var outstanding = await reports.GetOutstandingAsync();
        var row = outstanding.Rows.Single(r => r.Number.StartsWith("RPT-"));

        Assert.InRange(row.DaysOut, 19, 21);
    }

    /// <summary>
    /// A courier remitting net of its own fee leaves a part-paid order. That is
    /// a reconciliation to do rather than a debt to chase, so the report has to
    /// be able to tell the two apart.
    /// </summary>
    [Fact]
    public async Task A_part_paid_parcel_is_marked_as_such()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var today = ReportDate;

        await OrderAsync(services, today, SalesOrderStatus.Delivered, collected: 1000m);

        var row = (await reports.GetOutstandingAsync())
            .Rows.Single(r => r.Number.StartsWith("RPT-"));

        Assert.True(row.IsPartlyPaid);
        Assert.Equal(60m, row.Outstanding);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Grouped by the name on the line, not the product's name today. A product
    /// renamed last month did not sell under its new name, and a report saying
    /// otherwise cannot be reconciled against the invoices.
    /// </summary>
    [Fact]
    public async Task Best_sellers_are_named_as_they_were_sold()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var reports = services.GetRequiredService<SalesReportService>();

        await ClearAsync(services);

        var today = ReportDate;

        await OrderAsync(services, today, SalesOrderStatus.Delivered);
        await OrderAsync(services, today, SalesOrderStatus.Delivered);

        var top = await reports.GetTopProductsAsync(today, today);
        var row = top.Single(r => r.Sku == "RPT-SKU");

        Assert.Equal("Report product", row.ProductName);
        Assert.Equal(2, row.Orders);
        Assert.Equal(4m, row.Units);
    }
}
