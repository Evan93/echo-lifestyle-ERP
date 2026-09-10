using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// "Where is my order?" without an account.
///
/// Order number plus phone is a weak credential, and these tests are mostly
/// about it staying no weaker than that. The failure this file exists to
/// prevent is a lookup that answers with anything at all when only one half
/// matches - because order numbers run in sequence, and a page that confirms
/// which ones exist is a page that enumerates the business's orders.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StorefrontTrackingTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StorefrontTrackingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
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

    private async Task<long> SellableVariantAsync(IServiceProvider services)
    {
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
                services, supplierId, _warehouseId, _branchId, handle.VariantId, 10m, 300m);

            Assert.True((await products.SetPublishedAsync(handle.ProductId, true)).Succeeded);

            return handle.VariantId;
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

    /// <summary>One real website order, placed through the real checkout.</summary>
    private async Task<(string Number, string Phone)> PlacedOrderAsync(IServiceProvider services)
    {
        var carts = services.GetRequiredService<CartService>();
        var checkout = services.GetRequiredService<CheckoutService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var variantId = await SellableVariantAsync(services);
        var token = (await carts.AddAsync(null, variantId, 2m)).Value!;
        var districtId = await db.Districts.Where(d => d.Name == "Dhaka")
            .Select(d => d.Id).FirstAsync();

        var phone = UniquePhone();

        var placed = await checkout.PlaceAsync(new PlaceOrderRequest
        {
            CartToken = token,
            FullName = "Rumana Akter",
            Phone = phone,
            DistrictId = districtId,
            AreaOrThana = "Uttara",
            AddressLine = "House 9, Road 4, Sector 7",
        });

        Assert.True(placed.Succeeded, placed.Error);

        return (placed.Value!.Number, phone);
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_order_number_and_the_phone_together_find_the_order()
    {
        await using var scope = _fixture.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<StorefrontTrackingService>();

        var (number, phone) = await PlacedOrderAsync(scope.ServiceProvider);

        var found = await tracking.FindAsync(number, phone);

        Assert.NotNull(found);
        Assert.Equal(number, found!.Number);
        Assert.NotEmpty(found.Lines);
    }

    /// <summary>
    /// The rule the whole page rests on. Half a credential finds nothing, and
    /// the wrong phone against a real order number is indistinguishable from an
    /// order number that was never issued.
    /// </summary>
    [Fact]
    public async Task Half_a_credential_finds_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<StorefrontTrackingService>();

        var (number, phone) = await PlacedOrderAsync(scope.ServiceProvider);

        // A real order number with somebody else's phone.
        Assert.Null(await tracking.FindAsync(number, UniquePhone()));

        // A real phone with an order number that does not exist.
        Assert.Null(await tracking.FindAsync("SO-9999-9999", phone));

        // One half on its own, either way round.
        Assert.Null(await tracking.FindAsync(number, null));
        Assert.Null(await tracking.FindAsync(null, phone));
        Assert.Null(await tracking.FindAsync(string.Empty, string.Empty));
    }

    /// <summary>
    /// The number is typed again days later, from memory or from a saved
    /// contact. It will not come back in the form it went in as.
    /// </summary>
    [Fact]
    public async Task The_phone_is_matched_however_it_is_typed()
    {
        await using var scope = _fixture.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<StorefrontTrackingService>();

        var (number, phone) = await PlacedOrderAsync(scope.ServiceProvider);

        foreach (var typed in new[]
        {
            phone,
            "+88" + phone,
            "88" + phone,
            phone[..5] + "-" + phone[5..],
            " " + phone + " ",
        })
        {
            Assert.NotNull(await tracking.FindAsync(number, typed));
        }
    }

    [Fact]
    public async Task The_order_number_is_not_case_sensitive()
    {
        await using var scope = _fixture.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<StorefrontTrackingService>();

        var (number, phone) = await PlacedOrderAsync(scope.ServiceProvider);

        Assert.NotNull(await tracking.FindAsync(number.ToLowerInvariant(), phone));
        Assert.NotNull(await tracking.FindAsync("  " + number + "  ", phone));
    }

    /// <summary>
    /// A website order lands as a draft while somebody rings to confirm it.
    /// Telling a customer their order is a "draft" reads like it did not go
    /// through, so the page says what actually happened instead.
    /// </summary>
    [Fact]
    public async Task A_new_website_order_reads_as_received_rather_than_draft()
    {
        await using var scope = _fixture.CreateScope();
        var tracking = scope.ServiceProvider.GetRequiredService<StorefrontTrackingService>();

        var (number, phone) = await PlacedOrderAsync(scope.ServiceProvider);

        var found = await tracking.FindAsync(number, phone);

        Assert.Equal(SalesOrderStatus.Draft, found!.Status);
        Assert.Equal("Order received", found.StatusLabel);
        Assert.Contains("confirm", found.StatusExplanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What comes back is a delivery status, not a customer file. Somebody who
    /// does guess their way in must not learn what the goods cost the business.
    /// </summary>
    [Fact]
    public void Nothing_a_tracked_order_carries_can_be_cost_or_margin()
    {
        foreach (var type in new[] { typeof(TrackedOrder), typeof(TrackedOrderLine) })
        {
            var leaked = type.GetProperties()
                .Where(p => p.Name.Contains("Cost", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("Margin", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{type.Name}.{p.Name}")
                .ToList();

            Assert.True(
                leaked.Count == 0,
                $"A public tracking page must not carry buying prices: {string.Join(", ", leaked)}");
        }
    }
}
