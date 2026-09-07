using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Inventory;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Reading stock back, and putting a balance right when it disagrees with the
/// ledger.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StockQueryServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StockQueryServiceTests(DatabaseFixture fixture)
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

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        _branchId = await db.Branches.Where(b => b.IsActive).Select(b => b.Id).FirstAsync();
        _warehouseId = await db.Warehouses.Where(w => w.IsActive).Select(w => w.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<long> SupplierAsync(IServiceProvider services, string suffix)
    {
        var suppliers = services.GetRequiredService<SupplierAdminService>();

        var created = await suppliers.CreateAsync(new SaveSupplierRequest
        {
            Name = $"Stock supplier {suffix}",
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);
        return created.Value;
    }

    private static async Task<(long VariantId, string Sku)> VariantAsync(
        IServiceProvider services,
        string suffix,
        bool expiryTracked = false)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();
        var products = services.GetRequiredService<ProductAdminService>();

        var brand = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Stock brand {suffix}",
            IsActive = true,
        });

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Stock category {suffix}",
            IsActive = true,
        });

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Stock product {suffix}",
            BrandId = brand.Value,
            CategoryId = category.Value,
            Price = 800m,
            IsBatchTracked = expiryTracked,
            IsExpiryTracked = expiryTracked,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        var variant = (await products.GetAsync(created.Value))!.Variants.First();

        return (variant.Id, variant.Sku);
    }

    private async Task ReceiveAsync(
        IServiceProvider services,
        long supplierId,
        long variantId,
        decimal quantity,
        decimal cost,
        string? batchNumber = null,
        DateOnly? expiry = null)
    {
        var receipts = services.GetRequiredService<GoodsReceiptService>();

        var posted = await receipts.PostQuickPurchaseAsync(new QuickPurchaseRequest
        {
            SupplierId = supplierId,
            WarehouseId = _warehouseId,
            BranchId = _branchId,
            ReceiptDate = new DateOnly(2026, 9, 6),
            Lines =
            [
                new ReceiptLineInput
                {
                    ProductVariantId = variantId,
                    Quantity = quantity,
                    UnitCost = cost,
                    BatchNumber = batchNumber,
                    ExpiryDate = expiry,
                },
            ],
        });

        Assert.True(posted.Succeeded, posted.Error);
    }

    // -----------------------------------------------------------------------
    // Stock on hand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Stock_on_hand_sums_the_batches_of_one_product()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 10m, 300m);
        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 6m, 350m);

        var page = await stock.ListOnHandAsync(sku, 0, 50, null, false, null, false);

        var row = Assert.Single(page.Rows);

        Assert.Equal(16m, row.QuantityOnHand);
        Assert.Equal(0m, row.QuantityReserved);
        Assert.Equal(16m, row.QuantityAvailable);
        Assert.Equal(2, row.BatchCount);

        // Value is the sum of each batch at its own cost, not quantity times a
        // single figure.
        Assert.Equal(5100m, row.StockValue);

        // The average is a view over those actual costs - 5,100 over 16.
        Assert.Equal(318.75m, row.AverageUnitCost);
    }

    [Fact]
    public async Task Items_at_zero_are_hidden_unless_asked_for()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 5m, 100m);

        // Sell it all, the long way round - the ledger entry and the balance
        // together, exactly as a sale will do it.
        var balance = await db.StockBalances.FirstAsync(b => b.ProductVariantId == variantId);

        db.StockLedger.Add(new StockLedgerEntry
        {
            ProductVariantId = variantId,
            WarehouseId = _warehouseId,
            StockBatchId = balance.StockBatchId,
            MovementType = StockMovementType.Issue,
            QuantityChange = -5m,
            UnitCost = 100m,
            ValueChange = -500m,
            BranchId = _branchId,
            DocumentType = StockDocumentType.SalesOrder,
            DocumentId = 555_000_001,
            DocumentNumber = $"SO-{suffix}",
            OccurredAtUtc = DateTime.UtcNow,
            BusinessDate = new DateOnly(2026, 9, 6),
        });

        balance.QuantityOnHand = 0m;
        await db.SaveChangesAsync();

        Assert.Empty((await stock.ListOnHandAsync(sku, 0, 50, null, false, null, false)).Rows);
        Assert.Single((await stock.ListOnHandAsync(sku, 0, 50, null, false, null, true)).Rows);
    }

    [Fact]
    public async Task The_batch_breakdown_lists_the_oldest_expiry_first()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix, expiryTracked: true);

        await ReceiveAsync(
            scope.ServiceProvider, supplierId, variantId, 5m, 100m,
            $"LATE-{suffix}", new DateOnly(2027, 12, 1));

        await ReceiveAsync(
            scope.ServiceProvider, supplierId, variantId, 5m, 110m,
            $"SOON-{suffix}", new DateOnly(2027, 1, 1));

        var batches = await stock.GetBatchesAsync(variantId);

        // Not cosmetic: this is the order the stock should leave in, so whatever
        // sits at the top is what is about to be written off.
        Assert.Equal(2, batches.Count);
        Assert.Equal($"SOON-{suffix}", batches[0].BatchNumber);
        Assert.Equal(new DateOnly(2027, 1, 1), batches[0].ExpiryDate);
        Assert.Equal(550m, batches[0].Value);
    }

    // -----------------------------------------------------------------------
    // Movements
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Movements_are_listed_newest_first_and_findable_by_sku()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 4m, 200m);
        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 7m, 210m);

        var page = await stock.ListMovementsAsync(sku, 0, 50, null, false, null, null, null, null);

        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(7m, page.Rows[0].QuantityChange);
        Assert.All(page.Rows, r => Assert.Equal(StockMovementType.Receipt, r.MovementType));
    }

    [Fact]
    public async Task Movements_can_be_narrowed_to_one_product_and_a_date_range()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 3m, 100m);

        var inRange = await stock.ListMovementsAsync(
            null, 0, 50, null, false, variantId, _warehouseId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var outOfRange = await stock.ListMovementsAsync(
            null, 0, 50, null, false, variantId, _warehouseId,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Single(inRange.Rows);
        Assert.Empty(outOfRange.Rows);
    }

    // -----------------------------------------------------------------------
    // Expiry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Expired_stock_is_reported_alongside_what_is_about_to_expire()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix, expiryTracked: true);

        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);

        await ReceiveAsync(
            scope.ServiceProvider, supplierId, variantId, 8m, 150m, $"SOON-{suffix}", soon);

        // Backdated past its expiry, which receiving refuses outright - so it is
        // set directly, the way real stock quietly goes out of date on a shelf.
        var batch = await db.StockBatches.FirstAsync(b => b.BatchNumber == $"SOON-{suffix}");

        batch.ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        await db.SaveChangesAsync();

        var rows = await stock.ListExpiringAsync(90);
        var mine = rows.Where(r => r.ProductVariantId == variantId).ToList();

        var row = Assert.Single(mine);

        Assert.True(row.HasExpired);
        Assert.True(row.DaysRemaining < 0);
        Assert.Equal(1200m, row.ValueAtRisk);
    }

    [Fact]
    public async Task Stock_expiring_beyond_the_window_is_left_out()
    {
        await using var scope = _fixture.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockQueryService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix, expiryTracked: true);

        var distant = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400);

        await ReceiveAsync(
            scope.ServiceProvider, supplierId, variantId, 2m, 100m, $"FAR-{suffix}", distant);

        var near = await stock.ListExpiringAsync(90);
        var far = await stock.ListExpiringAsync(500);

        Assert.DoesNotContain(near, r => r.ProductVariantId == variantId);
        Assert.Contains(far, r => r.ProductVariantId == variantId);
    }

    // -----------------------------------------------------------------------
    // Rebuild
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_rebuild_puts_a_wrong_balance_right_from_the_ledger()
    {
        await using var scope = _fixture.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<StockBalanceRebuildService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 12m, 100m);

        // Corrupt it the way a bug would: the balance moves, the ledger does not.
        var balance = await db.StockBalances.FirstAsync(b => b.ProductVariantId == variantId);
        balance.QuantityOnHand = 99m;
        await db.SaveChangesAsync();

        var result = await rebuild.RebuildAsync();

        Assert.True(result.Succeeded, result.Error);

        var summary = result.Value!;
        Assert.False(summary.FoundNothingWrong);
        Assert.Contains(summary.Discrepancies, d => d.StockBatchId == balance.StockBatchId
                                                    && d.Was == 99m
                                                    && d.Now == 12m);

        var corrected = await db.StockBalances
            .AsNoTracking()
            .FirstAsync(b => b.Id == balance.Id);

        // The ledger is append-only, so the balance is what gets corrected -
        // never the history.
        Assert.Equal(12m, corrected.QuantityOnHand);
    }

    [Fact]
    public async Task A_rebuild_leaves_reservations_alone()
    {
        await using var scope = _fixture.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<StockBalanceRebuildService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 20m, 100m);

        var balance = await db.StockBalances.FirstAsync(b => b.ProductVariantId == variantId);
        balance.QuantityReserved = 6m;
        balance.QuantityOnHand = 4m;
        await db.SaveChangesAsync();

        await rebuild.RebuildAsync();

        var after = await db.StockBalances.AsNoTracking().FirstAsync(b => b.Id == balance.Id);

        // Reservations are commitments against orders that have not shipped.
        // They are not in the ledger, so a rebuild that recomputed them would
        // quietly release stock somebody has already been promised.
        Assert.Equal(20m, after.QuantityOnHand);
        Assert.Equal(6m, after.QuantityReserved);
    }

    [Fact]
    public async Task A_rebuild_recreates_a_balance_that_was_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<StockBalanceRebuildService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 9m, 100m);

        var balance = await db.StockBalances.FirstAsync(b => b.ProductVariantId == variantId);
        var batchId = balance.StockBatchId;

        db.StockBalances.Remove(balance);
        await db.SaveChangesAsync();

        var result = await rebuild.RebuildAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Created > 0);

        var recreated = await db.StockBalances
            .AsNoTracking()
            .FirstAsync(b => b.StockBatchId == batchId);

        Assert.Equal(9m, recreated.QuantityOnHand);
    }

    [Fact]
    public async Task A_rebuild_zeroes_a_balance_the_ledger_knows_nothing_about()
    {
        await using var scope = _fixture.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<StockBalanceRebuildService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 5m, 100m);

        // A second batch with a balance and no movements behind it: stock the
        // system claims to have but cannot account for.
        var phantom = new StockBatch
        {
            ProductVariantId = variantId,
            BatchNumber = $"PHANTOM-{suffix}",
            ReceivedDate = new DateOnly(2026, 9, 1),
            LandedUnitCost = 100m,
        };

        db.StockBatches.Add(phantom);
        await db.SaveChangesAsync();

        db.StockBalances.Add(new StockBalance
        {
            ProductVariantId = variantId,
            WarehouseId = _warehouseId,
            StockBatchId = phantom.Id,
            QuantityOnHand = 40m,
        });
        await db.SaveChangesAsync();

        var result = await rebuild.RebuildAsync();

        Assert.True(result.Value!.Zeroed > 0);

        var zeroed = await db.StockBalances
            .AsNoTracking()
            .FirstAsync(b => b.StockBatchId == phantom.Id);

        // Zeroed rather than deleted, so nothing attached to the row vanishes
        // with it.
        Assert.Equal(0m, zeroed.QuantityOnHand);
    }

    [Fact]
    public async Task A_rebuild_over_correct_data_changes_nothing_and_says_so()
    {
        await using var scope = _fixture.CreateScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<StockBalanceRebuildService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await ReceiveAsync(scope.ServiceProvider, supplierId, variantId, 3m, 100m);

        // Run twice: the second pass has nothing left to do, which is what
        // running it should normally look like.
        await rebuild.RebuildAsync();
        var second = await rebuild.RebuildAsync();

        Assert.True(second.Succeeded, second.Error);
        Assert.True(second.Value!.FoundNothingWrong);
        Assert.Contains("already matched", second.Value.Describe(), StringComparison.OrdinalIgnoreCase);
    }
}
