using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// What the public site is allowed to show.
///
/// These are the tests that matter most in the whole storefront, and they are
/// not about layout. A product that reaches the internet before it was meant to
/// is not a bug somebody notices in staging - it is a price, a photograph or a
/// product line becoming public while it was still being prepared. Every rule
/// in <see cref="StorefrontCatalogService"/> about visibility has a test here
/// that fails if it is loosened.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StorefrontCatalogServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StorefrontCatalogServiceTests(DatabaseFixture fixture)
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

    /// <summary>A published, priced product with stock - the normal case.</summary>
    private async Task<(VariantHandle Handle, string Slug)> PublishedProductAsync(
        IServiceProvider services,
        bool withStock = true)
    {
        var products = services.GetRequiredService<ProductAdminService>();

        var suffix = InventoryTestData.Unique();
        var handle = await InventoryTestData.VariantAsync(services, suffix);

        if (withStock)
        {
            var supplierId = await InventoryTestData.SupplierAsync(services, suffix);

            await InventoryTestData.ReceiveAsync(
                services, supplierId, _warehouseId, _branchId, handle.VariantId, 5m, 400m);
        }

        Assert.True((await products.SetPublishedAsync(handle.ProductId, true)).Succeeded);

        var slug = (await products.GetAsync(handle.ProductId))!.Slug;

        return (handle, slug);
    }

    /// <summary>
    /// Whether a product is reachable from the public site at all.
    ///
    /// Always through search rather than the home page: every test in this
    /// suite publishes products into the same database, so "is it in the newest
    /// eight" would pass or fail depending on what ran before it.
    /// </summary>
    private static async Task<bool> AppearsAsync(
        StorefrontCatalogService catalog,
        long productId,
        string search)
    {
        var (page, _) = await catalog.SearchAsync(search, ShopSort.Newest, 1);

        return page.Products.Any(p => p.ProductId == productId);
    }

    private static async Task<string> NameAsync(IServiceProvider services, long productId)
    {
        var products = services.GetRequiredService<ProductAdminService>();

        return (await products.GetAsync(productId))!.Name;
    }

    // -----------------------------------------------------------------------
    // Visibility
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_unpublished_product_is_invisible_everywhere()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = InventoryTestData.Unique();
        var handle = await InventoryTestData.VariantAsync(scope.ServiceProvider, suffix);
        var detail = (await products.GetAsync(handle.ProductId))!;

        // Priced, active, in the catalogue - and simply not published yet.
        // "Ready to sell" and "for sale" are different states on purpose.
        Assert.Null(await catalog.GetProductAsync(detail.Slug));
        Assert.False(await AppearsAsync(catalog, handle.ProductId, detail.Name));

        var (_, brandPage, _) = await catalog.GetBrandAsync(
            await BrandSlugAsync(scope.ServiceProvider, handle.BrandId), ShopSort.Newest, 1);

        Assert.DoesNotContain(brandPage.Products, p => p.ProductId == handle.ProductId);
    }

    [Fact]
    public async Task Unpublishing_takes_a_product_off_the_site()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var (handle, slug) = await PublishedProductAsync(scope.ServiceProvider);

        Assert.NotNull(await catalog.GetProductAsync(slug));

        Assert.True((await products.SetPublishedAsync(handle.ProductId, false)).Succeeded);

        // The route has to stop answering, not just the listings. A product
        // pulled from sale whose page still loads is still on the internet.
        Assert.Null(await catalog.GetProductAsync(slug));
    }

    /// <summary>
    /// Deactivating, not deleting - because deleting is refused.
    ///
    /// A product that has ever been priced may already sit on somebody's order,
    /// so the back office turns a delete into "deactivate it instead" (rule 9).
    /// That makes deactivation the route a real merchant takes to pull a line,
    /// and therefore the one the storefront has to honour.
    /// </summary>
    [Fact]
    public async Task A_deactivated_product_comes_off_the_site()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var (handle, slug) = await PublishedProductAsync(scope.ServiceProvider);
        var before = (await products.GetAsync(handle.ProductId))!;

        var updated = await products.UpdateAsync(handle.ProductId, new SaveProductRequest
        {
            Code = before.Code,
            Name = before.Name,
            Slug = before.Slug,
            BrandId = before.BrandId,
            UnitOfMeasureId = before.UnitOfMeasureId,
            ShortDescription = before.ShortDescription,
            LongDescription = before.LongDescription,
            HowToUse = before.HowToUse,
            Ingredients = before.Ingredients,
            IsBatchTracked = before.IsBatchTracked,
            IsExpiryTracked = before.IsExpiryTracked,
            ShelfLifeDays = before.ShelfLifeDays,
            IsActive = false,
            CategoryIds = before.CategoryIds,
            PrimaryCategoryId = before.PrimaryCategoryId,
        });

        Assert.True(updated.Succeeded, updated.Error);

        // Deactivating also unpublishes, so the product cannot come back to
        // life still on the site the next time somebody reactivates it.
        Assert.False((await products.GetAsync(handle.ProductId))!.IsPublished);

        Assert.Null(await catalog.GetProductAsync(slug));
        Assert.False(await AppearsAsync(catalog, handle.ProductId, before.Name));
    }

    /// <summary>
    /// The other half of rule 9, stated where the storefront can see it: a
    /// priced product is never hard-deleted, so no storefront query has to cope
    /// with a row vanishing out from under an order.
    /// </summary>
    [Fact]
    public async Task A_priced_product_cannot_be_deleted_at_all()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var (handle, _) = await PublishedProductAsync(scope.ServiceProvider, withStock: false);

        var deleted = await products.DeleteAsync(handle.ProductId);

        Assert.False(deleted.Succeeded);
        Assert.Contains("Deactivate it instead", deleted.Error);
    }

    /// <summary>
    /// A tile with no price is an invitation to ask for one, and the answer
    /// arrives by Messenger three days later. Better to leave it off the site
    /// until somebody prices it.
    /// </summary>
    [Fact]
    public async Task A_published_product_with_no_price_is_not_listed()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (handle, _) = await PublishedProductAsync(scope.ServiceProvider);
        var name = await NameAsync(scope.ServiceProvider, handle.ProductId);

        Assert.True(await AppearsAsync(catalog, handle.ProductId, name));

        // Close off every current price, as withdrawing one would.
        await db.PriceListItems
            .Where(i => i.ProductVariantId == handle.VariantId && i.EffectiveToUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.EffectiveToUtc, DateTime.UtcNow));

        Assert.False(await AppearsAsync(catalog, handle.ProductId, name));
    }

    // -----------------------------------------------------------------------
    // What a shopper sees
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Availability_is_on_hand_minus_reserved_not_on_hand()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (handle, slug) = await PublishedProductAsync(scope.ServiceProvider);

        var before = await catalog.GetProductAsync(slug);
        Assert.NotNull(before);
        Assert.True(before!.InStock);
        Assert.Equal(5m, before.Variants.Single().Available);

        // Everything on the shelf is now promised to somebody else's order.
        await db.StockBalances
            .Where(b => b.ProductVariantId == handle.VariantId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityReserved, b => b.QuantityOnHand));

        var after = await catalog.GetProductAsync(slug);

        // Rule 18. Stock already in somebody else's box is not for sale, and
        // showing it as available is how the same unit gets sold twice.
        Assert.NotNull(after);
        Assert.Equal(0m, after!.Variants.Single().Available);
        Assert.False(after.InStock);
    }

    [Fact]
    public async Task An_out_of_stock_product_is_still_listed_and_still_has_a_page()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var (handle, slug) = await PublishedProductAsync(scope.ServiceProvider, withStock: false);
        var name = await NameAsync(scope.ServiceProvider, handle.ProductId);

        var page = await catalog.GetProductAsync(slug);

        // Hiding it would tell a customer who searched for it that you never
        // carried it. Out of stock is information; absence is not.
        Assert.NotNull(page);
        Assert.False(page!.InStock);
        Assert.True(await AppearsAsync(catalog, handle.ProductId, name));
    }

    [Fact]
    public async Task Search_finds_a_product_by_name_and_by_sku()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var (handle, _) = await PublishedProductAsync(scope.ServiceProvider);
        var name = (await products.GetAsync(handle.ProductId))!.Name;

        Assert.True(await AppearsAsync(catalog, handle.ProductId, name));

        // By SKU too: staff answering a Messenger question search the way they
        // think, and they think in SKUs.
        Assert.True(await AppearsAsync(catalog, handle.ProductId, handle.Sku));
    }

    [Fact]
    public async Task An_empty_search_returns_nothing_rather_than_the_whole_catalogue()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var (page, _) = await catalog.SearchAsync("   ", ShopSort.Newest, 1);

        Assert.Empty(page.Products);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_category_lists_what_is_filed_under_it()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (handle, _) = await PublishedProductAsync(scope.ServiceProvider);

        var slug = await db.Categories
            .Where(c => c.Id == handle.CategoryId)
            .Select(c => c.Slug)
            .FirstAsync();

        var (category, page, _) = await catalog.GetCategoryAsync(slug, ShopSort.Newest, 1);

        Assert.NotNull(category);
        Assert.Contains(page.Products, p => p.ProductId == handle.ProductId);
    }

    [Fact]
    public async Task A_slug_that_does_not_exist_returns_nothing_rather_than_throwing()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        // Public URLs get mistyped, guessed and crawled. None of that is an
        // exception - the controller turns each of these into a 404.
        Assert.Null(await catalog.GetProductAsync("no-such-product"));
        Assert.Null((await catalog.GetCategoryAsync("no-such-category", ShopSort.Newest, 1)).Category);
        Assert.Null((await catalog.GetBrandAsync("no-such-brand", ShopSort.Newest, 1)).Brand);
    }

    [Fact]
    public async Task A_page_beyond_the_last_one_is_empty_rather_than_an_error()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var (page, _) = await catalog.SearchAsync("a", ShopSort.Newest, 9_999);

        Assert.Empty(page.Products);
    }

    private static async Task<string> BrandSlugAsync(IServiceProvider services, long brandId)
    {
        var db = services.GetRequiredService<EchoDbContext>();

        return await db.Brands.Where(b => b.Id == brandId).Select(b => b.Slug).FirstAsync();
    }
}
