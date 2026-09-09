using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// A basket becoming an order.
///
/// The tests that matter here are about what the browser is <em>not</em>
/// allowed to decide. A checkout form is the most exposed surface in the whole
/// system: anonymous, public, and one POST away from creating a customer, an
/// address and an order. Every price, every charge and every branch on that
/// order has to come from the server.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CheckoutServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public CheckoutServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Deliberately anonymous. A website order has no signed-in staff
        // member, and this is the state the storefront actually runs in.
        _fixture.CurrentUser.IsAuthenticated = false;
        _fixture.CurrentUser.UserType = null;
        _fixture.CurrentUser.IsOwner = false;
        _fixture.CurrentUser.UserId = null;
        _fixture.CurrentUser.UserName = null;
        _fixture.CurrentUser.BranchIds = [];
        _fixture.CurrentUser.Grants.Clear();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        _branchId = await db.Branches.Where(b => b.IsActive).Select(b => b.Id).FirstAsync();
        _warehouseId = await db.Warehouses.Where(w => w.IsActive).Select(w => w.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniquePhone() =>
        "018" + Random.Shared.Next(10_000_000, 99_999_999).ToString("D8");

    /// <summary>A published, priced, in-stock variant, ready to be bought.</summary>
    private async Task<(long VariantId, decimal Price)> SellableAsync(
        IServiceProvider services,
        decimal stock = 10m)
    {
        // Setting stock up needs a staff identity; the checkout itself does not.
        _fixture.CurrentUser.IsAuthenticated = true;
        _fixture.CurrentUser.UserType = UserType.Staff;
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.UserId = 1;
        _fixture.CurrentUser.BranchIds = [_branchId];

        try
        {
            var products = services.GetRequiredService<ProductAdminService>();
            var suffix = InventoryTestData.Unique();

            var handle = await InventoryTestData.VariantAsync(services, suffix);
            var supplierId = await InventoryTestData.SupplierAsync(services, suffix);

            await InventoryTestData.ReceiveAsync(
                services, supplierId, _warehouseId, _branchId, handle.VariantId, stock, 300m);

            Assert.True((await products.SetPublishedAsync(handle.ProductId, true)).Succeeded);

            var db = services.GetRequiredService<EchoDbContext>();

            var price = await db.PriceListItems
                .Where(i => i.ProductVariantId == handle.VariantId && i.EffectiveToUtc == null)
                .Select(i => i.UnitPrice)
                .FirstAsync();

            return (handle.VariantId, price);
        }
        finally
        {
            _fixture.CurrentUser.IsAuthenticated = false;
            _fixture.CurrentUser.UserType = null;
            _fixture.CurrentUser.IsOwner = false;
            _fixture.CurrentUser.UserId = null;
            _fixture.CurrentUser.BranchIds = [];
        }
    }

    private static async Task<long> DistrictAsync(IServiceProvider services, string name)
    {
        var db = services.GetRequiredService<EchoDbContext>();

        return await db.Districts.Where(d => d.Name == name).Select(d => d.Id).FirstAsync();
    }

    private PlaceOrderRequest Order(string token, long districtId, string? phone = null) => new()
    {
        CartToken = token,
        FullName = "Rumana Akter",
        Phone = phone ?? UniquePhone(),
        DistrictId = districtId,
        AreaOrThana = "Uttara",
        AddressLine = "House 9, Road 4, Sector 7",
    };

    // -----------------------------------------------------------------------
    // The basket
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_basket_reserves_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);

        var before = await db.StockBalances
            .Where(b => b.ProductVariantId == variantId)
            .SumAsync(b => b.QuantityReserved);

        var added = await carts.AddAsync(null, variantId, 3m);
        Assert.True(added.Succeeded, added.Error);

        var after = await db.StockBalances
            .Where(b => b.ProductVariantId == variantId)
            .SumAsync(b => b.QuantityReserved);

        // Two shoppers may hold the last jar. Reserving here would let anyone
        // empty the shelf by filling a basket and walking away.
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_basket_is_repriced_on_every_read_never_stored()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, price) = await SellableAsync(scope.ServiceProvider);
        var token = (await carts.AddAsync(null, variantId, 2m)).Value!;

        Assert.Equal(price * 2m, (await carts.GetAsync(token)).Subtotal);

        // The shop puts the price up while the basket sits open.
        await db.PriceListItems
            .Where(i => i.ProductVariantId == variantId && i.EffectiveToUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UnitPrice, price + 100m));

        // Rule 4. A basket left open for three days cannot hold Tuesday's price.
        Assert.Equal((price + 100m) * 2m, (await carts.GetAsync(token)).Subtotal);
    }

    [Fact]
    public async Task A_product_pulled_from_sale_blocks_checkout_rather_than_vanishing()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);
        var token = (await carts.AddAsync(null, variantId, 1m)).Value!;

        Assert.True((await carts.GetAsync(token)).CanCheckOut);

        var productId = await db.ProductVariants
            .Where(v => v.Id == variantId).Select(v => v.ProductId).FirstAsync();

        _fixture.CurrentUser.IsOwner = true;
        await products.SetPublishedAsync(productId, false);
        _fixture.CurrentUser.IsOwner = false;

        var cart = await carts.GetAsync(token);

        // Still listed, and still blocking. Silently dropping it is how somebody
        // receives half an order and blames the courier.
        Assert.Single(cart.Lines);
        Assert.False(cart.CanCheckOut);
        Assert.Single(cart.Problems);
    }

    [Fact]
    public async Task Adding_the_same_variant_twice_raises_the_quantity()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);

        var token = (await carts.AddAsync(null, variantId, 1m)).Value!;
        await carts.AddAsync(token, variantId, 2m);

        var cart = await carts.GetAsync(token);

        Assert.Single(cart.Lines);
        Assert.Equal(3m, cart.Lines[0].Quantity);
    }

    // -----------------------------------------------------------------------
    // Placing the order
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_anonymous_visitor_can_place_an_order_and_it_lands_as_a_draft()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, price) = await SellableAsync(scope.ServiceProvider);
        var token = (await carts.AddAsync(null, variantId, 2m)).Value!;
        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");

        var placed = await checkout.PlaceAsync(Order(token, districtId));

        Assert.True(placed.Succeeded, placed.Error);

        var order = await db.SalesOrders
            .AsNoTracking()
            .FirstAsync(o => o.Id == placed.Value!.SalesOrderId);

        // A draft. Nothing reserved, nothing moved, and somebody at Echo has to
        // say yes before it does - the same gate a Messenger order goes through.
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
        Assert.Equal(SalesChannel.Website, order.Channel);
        Assert.Equal(PaymentMethod.CashOnDelivery, order.PaymentMethod);

        Assert.Equal(price * 2m, order.SubTotal);

        var reserved = await db.StockBalances
            .Where(b => b.ProductVariantId == variantId)
            .SumAsync(b => b.QuantityReserved);

        Assert.Equal(0m, reserved);
    }

    [Fact]
    public async Task The_delivery_charge_comes_from_the_district_not_the_form()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var company = await db.Companies.AsNoTracking().FirstAsync();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);

        var inside = await DistrictAsync(scope.ServiceProvider, "Dhaka");
        var outside = await DistrictAsync(scope.ServiceProvider, "Rangpur");

        var dhakaToken = (await carts.AddAsync(null, variantId, 1m)).Value!;
        var dhaka = await checkout.PlaceAsync(Order(dhakaToken, inside));
        Assert.True(dhaka.Succeeded, dhaka.Error);

        var farToken = (await carts.AddAsync(null, variantId, 1m)).Value!;
        var far = await checkout.PlaceAsync(Order(farToken, outside));
        Assert.True(far.Succeeded, far.Error);

        // The browser posts a district id and nothing else that costs money.
        Assert.Equal(company.DeliveryChargeInsideCity, dhaka.Value!.DeliveryCharge);
        Assert.Equal(company.DeliveryChargeOutsideCity, far.Value!.DeliveryCharge);
        Assert.True(far.Value.DeliveryCharge >= dhaka.Value.DeliveryCharge);
    }

    [Fact]
    public async Task A_second_order_from_the_same_number_attaches_to_the_same_customer()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);
        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");
        var phone = UniquePhone();

        var first = await checkout.PlaceAsync(
            Order((await carts.AddAsync(null, variantId, 1m)).Value!, districtId, phone));

        // The same number, typed the way people actually type it.
        var second = await checkout.PlaceAsync(
            Order((await carts.AddAsync(null, variantId, 1m)).Value!, districtId,
                  "+88" + phone[..3] + "-" + phone[3..]));

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);

        var customers = await db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Id == first.Value!.SalesOrderId || o.Id == second.Value!.SalesOrderId)
            .Select(o => o.CustomerId)
            .Distinct()
            .ToListAsync();

        // Rule 15. One person, one record, one history - however they wrote it.
        Assert.Single(customers);
    }

    [Fact]
    public async Task A_blocked_customer_still_gets_a_draft_rather_than_a_refusal()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);
        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");
        var phone = UniquePhone();

        var first = await checkout.PlaceAsync(
            Order((await carts.AddAsync(null, variantId, 1m)).Value!, districtId, phone));

        Assert.True(first.Succeeded, first.Error);

        await db.Customers
            .Where(c => c.Phone == phone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsBlocked, true)
                .SetProperty(c => c.BlockReason, "Refused three parcels."));

        var second = await checkout.PlaceAsync(
            Order((await carts.AddAsync(null, variantId, 1m)).Value!, districtId, phone));

        // Telling somebody at the checkout that they are blocked only teaches
        // them to reorder from a new number. Staff see the flag and cancel it.
        Assert.True(second.Succeeded, second.Error);

        // And the half that makes the permissive half safe: the draft exists,
        // but the block still stops it going anywhere. A block that let stock
        // be committed would not be a block at all.
        _fixture.CurrentUser.IsAuthenticated = true;
        _fixture.CurrentUser.UserType = UserType.Staff;
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.UserId = 1;
        _fixture.CurrentUser.BranchIds = [_branchId];

        var orders = scope.ServiceProvider.GetRequiredService<SalesOrderService>();
        var confirmed = await orders.ConfirmAsync(second.Value!.SalesOrderId);

        Assert.False(confirmed.Succeeded);
        Assert.Contains("blocked", confirmed.Error);

        // Staff writing the same order by hand are stopped outright, with the
        // reason - they are present to be told, and can do something about it.
        var byHand = await orders.CreateAsync(new SaveOrderRequest
        {
            CustomerId = await db.Customers.Where(c => c.Phone == phone)
                .Select(c => c.Id).FirstAsync(),
            BranchId = _branchId,
            WarehouseId = _warehouseId,
            Lines = [new OrderLineInput { ProductVariantId = variantId, Quantity = 1m }],
        });

        Assert.False(byHand.Succeeded);
        Assert.Contains("blocked", byHand.Error);
    }

    [Fact]
    public async Task An_empty_basket_cannot_be_checked_out()
    {
        await using var scope = _fixture.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");

        var placed = await checkout.PlaceAsync(Order(CartService.NewToken(), districtId));

        Assert.False(placed.Succeeded);
    }

    [Fact]
    public async Task A_bad_phone_number_is_refused_before_anything_is_created()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);
        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");
        var token = (await carts.AddAsync(null, variantId, 1m)).Value!;

        var before = await db.Customers.CountAsync();

        var request = Order(token, districtId);
        request.Phone = "12345";

        var placed = await checkout.PlaceAsync(request);

        Assert.False(placed.Succeeded);
        Assert.Equal(nameof(PlaceOrderRequest.Phone), placed.Field);

        // Nothing half-created. A validation failure that leaves a customer row
        // behind fills the table with ghosts nobody can explain.
        Assert.Equal(before, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task A_placed_basket_cannot_be_ordered_again()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider);
        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");
        var token = (await carts.AddAsync(null, variantId, 1m)).Value!;

        Assert.True((await checkout.PlaceAsync(Order(token, districtId))).Succeeded);

        // The browser still holds the cookie. A refresh, a back button or a
        // second tab must not buy the same basket twice.
        Assert.True((await carts.GetAsync(token)).IsEmpty);
        Assert.False((await checkout.PlaceAsync(Order(token, districtId))).Succeeded);
    }

    [Fact]
    public async Task More_of_something_than_is_on_the_shelf_cannot_be_checked_out()
    {
        await using var scope = _fixture.CreateScope();
        var carts = scope.ServiceProvider.GetRequiredService<CartService>();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

        var (variantId, _) = await SellableAsync(scope.ServiceProvider, stock: 2m);
        var districtId = await DistrictAsync(scope.ServiceProvider, "Dhaka");
        var token = (await carts.AddAsync(null, variantId, 5m)).Value!;

        var cart = await carts.GetAsync(token);

        Assert.False(cart.CanCheckOut);
        Assert.Contains("Only", cart.Lines[0].Problem);
        Assert.False((await checkout.PlaceAsync(Order(token, districtId))).Succeeded);
    }
}
