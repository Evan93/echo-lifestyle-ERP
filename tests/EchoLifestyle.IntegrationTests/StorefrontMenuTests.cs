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
/// What the header menu is allowed to advertise.
///
/// The panel now offers brands as well as sub-categories, which means it makes
/// two promises it did not before: that the brand has something under this
/// heading, and that there are exactly that many products of it. Both are
/// checked here, along with the rule the whole storefront rests on - that no
/// navigation aid can name something the catalogue would not show.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StorefrontMenuTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;

    public StorefrontMenuTests(DatabaseFixture fixture)
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

        _fixture.CurrentUser.BranchIds = [_branchId];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -----------------------------------------------------------------------

    private static async Task<long> CategoryAsync(
        IServiceProvider services,
        string name,
        long? parentId = null)
    {
        var categories = services.GetRequiredService<CategoryAdminService>();

        var result = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = name,
            ParentId = parentId,
            ShowInMenu = true,
            IsActive = true,
        });

        Assert.True(result.Succeeded, result.Error);

        return result.Value;
    }

    private static async Task<long> BrandAsync(IServiceProvider services, string name)
    {
        var brands = services.GetRequiredService<BrandAdminService>();

        var result = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = name,
            IsActive = true,
        });

        Assert.True(result.Succeeded, result.Error);

        return result.Value;
    }

    private static async Task<long> ProductAsync(
        IServiceProvider services,
        long brandId,
        long categoryId,
        bool published = true)
    {
        var products = services.GetRequiredService<ProductAdminService>();

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Menu product {InventoryTestData.Unique()}",
            BrandId = brandId,
            CategoryId = categoryId,
            Price = 750m,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        if (published)
        {
            Assert.True((await products.SetPublishedAsync(created.Value, true)).Succeeded);
        }

        return created.Value;
    }

    private static async Task<ShopCategory> MenuEntryAsync(
        IServiceProvider services,
        long categoryId)
    {
        var catalog = services.GetRequiredService<StorefrontCatalogService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var slug = await db.Categories
            .Where(c => c.Id == categoryId).Select(c => c.Slug).FirstAsync();

        var menu = await catalog.GetMenuAsync();
        var entry = menu.FirstOrDefault(c => c.Slug == slug);

        Assert.NotNull(entry);

        return entry!;
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_heading_lists_the_brands_stocked_under_it_with_their_counts()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        var suffix = InventoryTestData.Unique();
        var categoryId = await CategoryAsync(services, $"Menu category {suffix}");
        var brandA = await BrandAsync(services, $"Menu brand A {suffix}");
        var brandB = await BrandAsync(services, $"Menu brand B {suffix}");

        await ProductAsync(services, brandA, categoryId);
        await ProductAsync(services, brandA, categoryId);
        await ProductAsync(services, brandB, categoryId);

        var entry = await MenuEntryAsync(services, categoryId);

        // Most stocked first, because that is the order somebody scanning a
        // panel is best served by.
        Assert.Equal(brandA, entry.Brands[0].Id);
        Assert.Equal(2, entry.Brands[0].Count);
        Assert.Contains(entry.Brands, b => b.Id == brandB && b.Count == 1);
    }

    /// <summary>
    /// The panel is a navigation aid, not a second door. Anything the catalogue
    /// would refuse to show must not be named here either - a brand appearing
    /// in the menu with nothing behind it is a dead end that looks like stock.
    /// </summary>
    [Fact]
    public async Task An_unpublished_product_does_not_put_its_brand_in_the_menu()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        var suffix = InventoryTestData.Unique();
        var categoryId = await CategoryAsync(services, $"Menu category {suffix}");
        var brandId = await BrandAsync(services, $"Menu brand {suffix}");

        await ProductAsync(services, brandId, categoryId, published: false);

        var entry = await MenuEntryAsync(services, categoryId);

        Assert.DoesNotContain(entry.Brands, b => b.Id == brandId);
    }

    /// <summary>
    /// The count has to match what the click lands on. The panel links into the
    /// category filtered by brand, and that page counts everything at or under
    /// the heading - so a product filed two levels down belongs in the number.
    /// </summary>
    [Fact]
    public async Task A_brand_stocked_only_in_a_child_still_shows_under_the_parent()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        var suffix = InventoryTestData.Unique();
        var parentId = await CategoryAsync(services, $"Menu parent {suffix}");
        var childId = await CategoryAsync(services, $"Menu child {suffix}", parentId);
        var brandId = await BrandAsync(services, $"Menu brand {suffix}");

        await ProductAsync(services, brandId, childId);

        var entry = await MenuEntryAsync(services, parentId);

        Assert.Contains(entry.Brands, b => b.Id == brandId && b.Count == 1);
        Assert.Contains(entry.Children, c => c.Id == childId);
    }

    /// <summary>
    /// A deactivated brand comes off the site, so it comes off the menu with it.
    /// </summary>
    [Fact]
    public async Task Deactivating_a_brand_takes_it_out_of_the_menu()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var brands = services.GetRequiredService<BrandAdminService>();

        var suffix = InventoryTestData.Unique();
        var categoryId = await CategoryAsync(services, $"Menu category {suffix}");
        var brandId = await BrandAsync(services, $"Menu brand {suffix}");

        await ProductAsync(services, brandId, categoryId);

        Assert.Contains((await MenuEntryAsync(services, categoryId)).Brands, b => b.Id == brandId);

        var update = await brands.UpdateAsync(brandId, new SaveBrandRequest
        {
            Name = $"Menu brand {suffix}",
            IsActive = false,
        });

        Assert.True(update.Succeeded, update.Error);

        Assert.DoesNotContain(
            (await MenuEntryAsync(services, categoryId)).Brands, b => b.Id == brandId);
    }

    /// <summary>
    /// A menu that grew with the catalogue would eventually be a second
    /// catalogue. Past the cap the panel stops and the category page's own
    /// sidebar carries the rest.
    /// </summary>
    [Fact]
    public async Task The_brand_list_in_a_panel_is_capped()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        var suffix = InventoryTestData.Unique();
        var categoryId = await CategoryAsync(services, $"Menu category {suffix}");

        for (var i = 0; i < 11; i++)
        {
            var brandId = await BrandAsync(services, $"Menu brand {i} {suffix}");
            await ProductAsync(services, brandId, categoryId);
        }

        var entry = await MenuEntryAsync(services, categoryId);

        Assert.Equal(8, entry.Brands.Count);
    }
}
