using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// What the site tells search engines exists.
///
/// The same rule as the filters, and for the same reason: a sitemap is a
/// listing, so it narrows what the catalogue shows and never opens a second
/// door onto it. Getting this wrong is worse than getting a filter wrong -
/// a filter shows an unpublished product to whoever found the URL, a sitemap
/// hands it to Google and asks for it to be indexed.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StorefrontSitemapTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StorefrontSitemapTests(DatabaseFixture fixture)
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

    /// <summary>A published, priced product with stock, and its slug.</summary>
    private async Task<(long ProductId, string Slug)> PublishedAsync(IServiceProvider services)
    {
        var products = services.GetRequiredService<ProductAdminService>();
        var db = services.GetRequiredService<EchoDbContext>();
        var suffix = InventoryTestData.Unique();

        var handle = await InventoryTestData.VariantAsync(services, suffix);
        var supplierId = await InventoryTestData.SupplierAsync(services, suffix);

        await InventoryTestData.ReceiveAsync(
            services, supplierId, _warehouseId, _branchId, handle.VariantId, 5m, 200m);

        Assert.True((await products.SetPublishedAsync(handle.ProductId, true)).Succeeded);

        var slug = await db.Products
            .Where(p => p.Id == handle.ProductId).Select(p => p.Slug).FirstAsync();

        return (handle.ProductId, slug);
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_published_product_is_listed()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();

        var (_, slug) = await PublishedAsync(scope.ServiceProvider);

        var sitemap = await catalog.GetSitemapAsync();

        Assert.Contains(sitemap.Products, e => e.Slug == slug);
    }

    /// <summary>
    /// The rule the file exists for. Unpublishing takes a product off the site,
    /// so it has to come off the sitemap too - otherwise the shop keeps asking
    /// to be indexed for a page it will not serve.
    /// </summary>
    [Fact]
    public async Task An_unpublished_product_is_not_offered_to_a_search_engine()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var (productId, slug) = await PublishedAsync(scope.ServiceProvider);

        Assert.Contains((await catalog.GetSitemapAsync()).Products, e => e.Slug == slug);

        Assert.True((await products.SetPublishedAsync(productId, false)).Succeeded);

        Assert.DoesNotContain((await catalog.GetSitemapAsync()).Products, e => e.Slug == slug);
    }

    /// <summary>
    /// Deactivating does the same. Two different switches, one visible
    /// consequence - which is the point of having a single definition of
    /// "sellable" rather than a sitemap query with its own opinion.
    /// </summary>
    [Fact]
    public async Task A_deactivated_product_comes_off_the_sitemap_too()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var (productId, slug) = await PublishedAsync(scope.ServiceProvider);

        var before = (await products.GetAsync(productId))!;

        var updated = await products.UpdateAsync(productId, new SaveProductRequest
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

        Assert.DoesNotContain((await catalog.GetSitemapAsync()).Products, e => e.Slug == slug);
    }

    /// <summary>
    /// The sitemap is generated, not stored, so a slug edited in the back
    /// office is the slug submitted. A stale copy is how a shop asks to be
    /// indexed for URLs that now 404.
    /// </summary>
    [Fact]
    public async Task The_listing_follows_the_catalogue_rather_than_a_stored_copy()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var before = (await catalog.GetSitemapAsync()).Products.Count;

        await PublishedAsync(scope.ServiceProvider);

        var after = await catalog.GetSitemapAsync();

        Assert.Equal(before + 1, after.Products.Count);

        // Every listed slug resolves to a row that is genuinely on the site.
        var slugs = after.Products.Select(e => e.Slug).ToList();

        var visible = await db.Products
            .Where(p => slugs.Contains(p.Slug) && p.IsPublished && p.IsActive)
            .CountAsync();

        Assert.Equal(slugs.Count, visible);
    }

    /// <summary>
    /// An empty category is a thin page. Submitting a pile of them is how a
    /// small catalogue looks to a search engine like a large bad one.
    /// </summary>
    [Fact]
    public async Task A_category_with_nothing_sellable_in_it_is_not_listed()
    {
        await using var scope = _fixture.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<StorefrontCatalogService>();
        var categories = scope.ServiceProvider
            .GetRequiredService<Application.Catalog.Categories.CategoryAdminService>();

        var created = await categories.CreateAsync(
            new Application.Catalog.Categories.SaveCategoryRequest
            {
                Name = $"Empty category {InventoryTestData.Unique()}",
                IsActive = true,
            });

        Assert.True(created.Succeeded, created.Error);

        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
        var slug = await db.Categories
            .Where(c => c.Id == created.Value).Select(c => c.Slug).FirstAsync();

        Assert.DoesNotContain((await catalog.GetSitemapAsync()).Categories, e => e.Slug == slug);
    }
}
