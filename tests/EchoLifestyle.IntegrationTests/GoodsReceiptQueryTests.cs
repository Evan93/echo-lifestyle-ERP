using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Domain.Purchasing;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Reading receipts back, and the variant search the receiving screen types
/// into.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class GoodsReceiptQueryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public GoodsReceiptQueryTests(DatabaseFixture fixture)
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

    private static async Task<long> SupplierAsync(IServiceProvider services, string name)
    {
        var suppliers = services.GetRequiredService<SupplierAdminService>();

        var created = await suppliers.CreateAsync(new SaveSupplierRequest { Name = name, IsActive = true });
        Assert.True(created.Succeeded, created.Error);

        return created.Value;
    }

    private static async Task<(long VariantId, string Sku)> VariantAsync(
        IServiceProvider services,
        string suffix,
        string? productName = null,
        string? barcode = null)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();
        var products = services.GetRequiredService<ProductAdminService>();

        var brand = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Query brand {suffix}",
            IsActive = true,
        });

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Query category {suffix}",
            IsActive = true,
        });

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = productName ?? $"Query product {suffix}",
            BrandId = brand.Value,
            CategoryId = category.Value,
            Barcode = barcode,
            Price = 500m,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        var variant = (await products.GetAsync(created.Value))!.Variants.First();

        return (variant.Id, variant.Sku);
    }

    private QuickPurchaseRequest Request(long supplierId, long variantId, decimal quantity, decimal cost) => new()
    {
        SupplierId = supplierId,
        WarehouseId = _warehouseId,
        BranchId = _branchId,
        ReceiptDate = new DateOnly(2026, 9, 6),
        Lines = [new ReceiptLineInput { ProductVariantId = variantId, Quantity = quantity, UnitCost = cost }],
    };

    // -----------------------------------------------------------------------
    // Listing and detail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_posted_receipt_appears_in_the_list_with_its_totals()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, $"List supplier {suffix}");
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        var posted = await receipts.PostQuickPurchaseAsync(Request(supplierId, variantId, 12m, 250m));
        Assert.True(posted.Succeeded, posted.Error);

        var page = await receipts.ListAsync(suffix, 0, 50, null, false, null);

        var row = Assert.Single(page.Rows);

        Assert.Equal(GoodsReceiptStatus.Posted, row.Status);
        Assert.Equal(1, row.LineCount);
        Assert.Equal(12m, row.TotalQuantity);
        Assert.Equal(3000m, row.GrandTotal);
    }

    [Fact]
    public async Task The_list_can_be_filtered_to_one_supplier()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var mine = await SupplierAsync(scope.ServiceProvider, $"Filter mine {suffix}");
        var theirs = await SupplierAsync(scope.ServiceProvider, $"Filter theirs {suffix}");
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await receipts.PostQuickPurchaseAsync(Request(mine, variantId, 1m, 10m));
        await receipts.PostQuickPurchaseAsync(Request(theirs, variantId, 1m, 10m));

        var filtered = await receipts.ListAsync(null, 0, 50, null, false, mine);

        Assert.All(filtered.Rows, r => Assert.Equal($"Filter mine {suffix}", r.SupplierName));
        Assert.Single(filtered.Rows);
    }

    [Fact]
    public async Task A_receipt_can_be_found_by_the_sku_it_contains()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, $"Sku search {suffix}");
        var (variantId, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        await receipts.PostQuickPurchaseAsync(Request(supplierId, variantId, 3m, 100m));

        // "Where did this SKU come from" is the question somebody asks holding
        // the product, not holding the paperwork.
        var page = await receipts.ListAsync(sku, 0, 50, null, false, null);

        Assert.Single(page.Rows);
    }

    [Fact]
    public async Task The_detail_view_shows_landed_cost_and_hides_generated_batch_numbers()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, $"Detail supplier {suffix}");
        var (variantId, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        var posted = await receipts.PostQuickPurchaseAsync(Request(supplierId, variantId, 4m, 125m));

        var detail = await receipts.GetAsync(posted.Value);

        Assert.NotNull(detail);
        Assert.Equal(GoodsReceiptStatus.Posted, detail!.Status);
        Assert.Equal(500m, detail.GrandTotal);

        var line = Assert.Single(detail.Lines);

        Assert.Equal(sku, line.Sku);
        Assert.Equal(125m, line.LandedUnitCost);

        // The batch exists, but nobody asked for it, so the screen does not
        // present it as something they chose.
        Assert.True(line.BatchWasGenerated);
    }

    [Fact]
    public async Task Newest_receipts_come_first_by_default()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, $"Order supplier {suffix}");
        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix);

        await receipts.PostQuickPurchaseAsync(Request(supplierId, variantId, 1m, 10m));
        await receipts.PostQuickPurchaseAsync(Request(supplierId, variantId, 1m, 10m));

        var page = await receipts.ListAsync(null, 0, 50, null, false, supplierId);

        // The receipt somebody wants is almost always the one just posted.
        Assert.Equal(2, page.Rows.Count);
        Assert.True(string.CompareOrdinal(page.Rows[0].Number, page.Rows[1].Number) > 0);
    }

    [Fact]
    public async Task Warehouse_options_put_the_branchs_primary_first()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var options = await receipts.GetWarehouseOptionsAsync(_branchId);

        Assert.NotEmpty(options);

        // The seeded branch has a primary warehouse; it should not be something
        // the user has to go looking for.
        if (options.Any(o => o.IsPrimary))
        {
            Assert.True(options[0].IsPrimary);
        }
    }

    // -----------------------------------------------------------------------
    // Variant lookup
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_exact_sku_match_is_ranked_first()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var (_, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        var matches = await products.LookupVariantsAsync(sku);

        // Typing a full SKU, or scanning it, should not land third behind two
        // products whose names happen to contain the same characters.
        Assert.NotEmpty(matches);
        Assert.Equal(sku, matches[0].Sku);
    }

    [Fact]
    public async Task A_barcode_finds_its_variant()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var barcode = $"890{suffix}";

        var (variantId, _) = await VariantAsync(scope.ServiceProvider, suffix, barcode: barcode);

        var matches = await products.LookupVariantsAsync(barcode);

        Assert.Equal(variantId, Assert.Single(matches).Id);
    }

    [Fact]
    public async Task The_lookup_offers_what_the_product_last_cost()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, $"Cost memory {suffix}");
        var (variantId, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        await receipts.PostQuickPurchaseAsync(Request(supplierId, variantId, 5m, 275m));

        var match = Assert.Single(await products.LookupVariantsAsync(sku));

        // So the cost field can be pre-filled rather than remembered.
        Assert.Equal(275m, match.LastUnitCost);
        Assert.Equal(500m, match.CurrentPrice);
    }

    [Fact]
    public async Task A_lookup_of_one_character_returns_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        // Otherwise the first keystroke scans the whole catalogue.
        Assert.Empty(await products.LookupVariantsAsync("a"));
        Assert.Empty(await products.LookupVariantsAsync(" "));
        Assert.Empty(await products.LookupVariantsAsync(null));
    }

    [Fact]
    public async Task An_inactive_product_is_not_offered_for_receiving()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var (_, sku) = await VariantAsync(scope.ServiceProvider, suffix);

        var detail = (await products.LookupVariantsAsync(sku)).Single();

        var product = await scope.ServiceProvider.GetRequiredService<EchoDbContext>()
            .Products
            .FirstAsync(p => p.Variants.Any(v => v.Id == detail.Id));

        product.IsActive = false;
        await scope.ServiceProvider.GetRequiredService<EchoDbContext>().SaveChangesAsync();

        // Receiving something the business has retired is nearly always a
        // mistake, and the posting service refuses it anyway.
        Assert.Empty(await products.LookupVariantsAsync(sku));
    }
}
