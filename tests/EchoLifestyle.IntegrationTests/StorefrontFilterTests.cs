using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Narrowing a listing down.
///
/// The rule these all circle is that a filter must never be able to show
/// something the catalogue would not have shown anyway. Filters narrow; they do
/// not open a second door onto unpublished or unpriced stock.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StorefrontFilterTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StorefrontFilterTests(DatabaseFixture fixture)
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
    /// One category with several products in it, so a filter has something to
    /// narrow. Everything is built through the real services.
    /// </summary>
    private async Task<Shelf> ShelfAsync(IServiceProvider services)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();
        var products = services.GetRequiredService<ProductAdminService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var suffix = InventoryTestData.Unique();

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Filter category {suffix}",
            IsActive = true,
        });

        Assert.True(category.Succeeded, category.Error);

        var a = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Filter brand A {suffix}",
            IsActive = true,
        });

        var b = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Filter brand B {suffix}",
            IsActive = true,
        });

        Assert.True(a.Succeeded, a.Error);
        Assert.True(b.Succeeded, b.Error);

        var categoryId = category.Value;
        var brandA = a.Value;
        var brandB = b.Value;

        async Task<long> Make(long brandId, decimal price, bool withStock)
        {
            var created = await products.QuickCreateAsync(new QuickCreateProductRequest
            {
                Name = $"Filter product {InventoryTestData.Unique()}",
                BrandId = brandId,
                CategoryId = categoryId,
                Price = price,
                IsActive = true,
            });

            Assert.True(created.Succeeded, created.Error);

            if (withStock)
            {
                var variantId = (await products.GetAsync(created.Value))!.Variants.First().Id;
                var supplierId = await InventoryTestData.SupplierAsync(
                    services, InventoryTestData.Unique());

                await InventoryTestData.ReceiveAsync(
                    services, supplierId, _warehouseId, _branchId, variantId, 4m, 100m);
            }

            Assert.True((await products.SetPublishedAsync(created.Value, true)).Succeeded);

            return created.Value;
        }

        var cheapA = await Make(brandA, 500m, withStock: true);
        var dearA = await Make(brandA, 2500m, withStock: true);
        var soldOutB = await Make(brandB, 1200m, withStock: false);

        var slug = await db.Categories
            .Where(c => c.Id == categoryId).Select(c => c.Slug).FirstAsync();

        return new Shelf
        {
            CategorySlug = slug,
            BrandAId = brandA,
            BrandBId = brandB,
            CheapAId = cheapA,
            DearAId = dearA,
            SoldOutBId = soldOutB,
        };
    }

    private sealed class Shelf
    {
        public string CategorySlug { get; init; } = string.Empty;

        public long BrandAId { get; init; }

        public long BrandBId { get; init; }

        public long CheapAId { get; init; }

        public long DearAId { get; init; }

        public long SoldOutBId { get; init; }
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task Filtering_by_brand_keeps_only_that_brand()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);

        var (_, page, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1,
            new ShopFilters { BrandIds = [shelf.BrandAId] });

        Assert.Equal(2, page.TotalCount);
        Assert.DoesNotContain(page.Products, p => p.ProductId == shelf.SoldOutBId);
    }

    /// <summary>
    /// The facet list is computed before the brand filter, so ticking one brand
    /// leaves the others on screen. Otherwise somebody who ticks the wrong one
    /// cannot widen their own search without pressing back.
    /// </summary>
    [Fact]
    public async Task Choosing_a_brand_does_not_remove_the_other_brands_from_the_sidebar()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);

        var (_, _, facets) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1,
            new ShopFilters { BrandIds = [shelf.BrandAId] });

        Assert.Contains(facets.Brands, b => b.Id == shelf.BrandAId && b.Count == 2);
        Assert.Contains(facets.Brands, b => b.Id == shelf.BrandBId && b.Count == 1);
    }

    [Fact]
    public async Task A_price_range_uses_the_cheapest_variant_of_each_product()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);

        var (_, under, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1,
            new ShopFilters { MaxPrice = 1500m });

        Assert.Contains(under.Products, p => p.ProductId == shelf.CheapAId);
        Assert.DoesNotContain(under.Products, p => p.ProductId == shelf.DearAId);

        var (_, over, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1,
            new ShopFilters { MinPrice = 2000m });

        Assert.Contains(over.Products, p => p.ProductId == shelf.DearAId);
        Assert.DoesNotContain(over.Products, p => p.ProductId == shelf.CheapAId);
    }

    [Fact]
    public async Task In_stock_only_hides_what_cannot_be_bought_today()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);

        var (_, all, _) = await catalog.GetCategoryAsync(shelf.CategorySlug, ShopSort.Newest, 1);

        // Out of stock is still listed by default - somebody who searched for it
        // should learn it exists.
        Assert.Contains(all.Products, p => p.ProductId == shelf.SoldOutBId);

        var (_, filtered, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1,
            new ShopFilters { InStockOnly = true });

        Assert.DoesNotContain(filtered.Products, p => p.ProductId == shelf.SoldOutBId);
        Assert.Equal(2, filtered.TotalCount);
    }

    [Fact]
    public async Task Filters_stack_rather_than_replacing_one_another()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);

        var (_, page, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1,
            new ShopFilters
            {
                BrandIds = [shelf.BrandAId],
                MaxPrice = 1500m,
                InStockOnly = true,
            });

        Assert.Single(page.Products);
        Assert.Equal(shelf.CheapAId, page.Products[0].ProductId);
    }

    /// <summary>
    /// The rule underneath all of these: a filter narrows what the catalogue
    /// already shows. It is not a second route onto unpublished stock.
    /// </summary>
    [Fact]
    public async Task No_filter_can_surface_an_unpublished_product()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);

        Assert.True((await products.SetPublishedAsync(shelf.CheapAId, false)).Succeeded);

        foreach (var filters in new[]
        {
            new ShopFilters { BrandIds = [shelf.BrandAId] },
            new ShopFilters { MinPrice = 1m, MaxPrice = 999999m },
            new ShopFilters { InStockOnly = true },
            new ShopFilters { OnOfferOnly = true },
        })
        {
            var (_, page, _) = await catalog.GetCategoryAsync(
                shelf.CategorySlug, ShopSort.Newest, 1, filters);

            Assert.DoesNotContain(page.Products, p => p.ProductId == shelf.CheapAId);
        }
    }

    [Fact]
    public async Task Filtering_survives_paging_and_an_out_of_range_page_is_simply_empty()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var shelf = await ShelfAsync(scope.ServiceProvider);
        var filters = new ShopFilters { BrandIds = [shelf.BrandAId] };

        var (_, first, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 1, filters);

        var (_, far, _) = await catalog.GetCategoryAsync(
            shelf.CategorySlug, ShopSort.Newest, 99, filters);

        // The count stays the filtered count on every page - a pager that
        // reported the unfiltered total would offer pages that do not exist.
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, far.TotalCount);
        Assert.Empty(far.Products);
    }
}
