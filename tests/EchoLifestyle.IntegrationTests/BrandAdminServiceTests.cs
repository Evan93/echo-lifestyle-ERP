using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class BrandAdminServiceTests
{
    private readonly DatabaseFixture _fixture;

    public BrandAdminServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static SaveBrandRequest Request(string name, string? slug = null) => new()
    {
        Name = name,
        Slug = slug,
        OriginCountry = "South Korea",
        IsActive = true,
    };

    [Fact]
    public async Task Creating_a_brand_derives_a_slug_from_its_name()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var suffix = Unique();
        var created = await service.CreateAsync(Request($"The Ordinary {suffix}"));

        Assert.True(created.Succeeded, created.Error);

        var detail = await service.GetAsync(created.Value);

        Assert.Equal($"the-ordinary-{suffix}", detail!.Slug);
    }

    [Fact]
    public async Task A_name_that_produces_no_slug_asks_for_one_rather_than_saving_a_blank_address()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        // Bangla folds to an empty slug. Saving that would give the brand an
        // address of "/", which would collide with every other such brand.
        var created = await service.CreateAsync(Request("রূপচর্চা"));

        Assert.False(created.Succeeded);
        Assert.Equal(nameof(SaveBrandRequest.Slug), created.Field);
    }

    [Fact]
    public async Task Two_brands_cannot_share_a_name()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var name = $"Cetaphil {Unique()}";

        Assert.True((await service.CreateAsync(Request(name))).Succeeded);

        var duplicate = await service.CreateAsync(Request(name));

        Assert.False(duplicate.Succeeded);
        Assert.Equal(nameof(SaveBrandRequest.Name), duplicate.Field);
    }

    [Fact]
    public async Task A_colliding_generated_slug_is_suffixed_rather_than_refused()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var suffix = Unique();

        // Two genuinely different names that fold to the same slug: everything
        // outside the Latin alphabet is dropped, so a Bangla subtitle leaves
        // nothing behind. This is a realistic case here, not a contrived one.
        var first = await service.CreateAsync(Request($"Cerave {suffix}"));
        var second = await service.CreateAsync(Request($"Cerave {suffix} রূপচর্চা"));

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);

        var firstDetail = await service.GetAsync(first.Value);
        var secondDetail = await service.GetAsync(second.Value);

        Assert.Equal($"cerave-{suffix}", firstDetail!.Slug);
        Assert.Equal($"cerave-{suffix}-2", secondDetail!.Slug);
    }

    [Fact]
    public async Task A_slug_the_user_typed_is_refused_on_collision_rather_than_quietly_changed()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var slug = $"taken-{Unique()}";

        Assert.True((await service.CreateAsync(Request($"First {Unique()}", slug))).Succeeded);

        var second = await service.CreateAsync(Request($"Second {Unique()}", slug));

        // Silently publishing "taken-2" would give the brand an address nobody
        // chose and nobody would notice until a link broke.
        Assert.False(second.Succeeded);
        Assert.Equal(nameof(SaveBrandRequest.Slug), second.Field);
    }

    [Fact]
    public async Task A_malformed_typed_slug_is_rejected()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var created = await service.CreateAsync(Request($"Brand {Unique()}", "Not A Slug"));

        Assert.False(created.Succeeded);
        Assert.Equal(nameof(SaveBrandRequest.Slug), created.Field);
    }

    [Fact]
    public async Task A_brand_with_no_products_can_be_deleted_and_frees_its_name()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var name = $"Transient {Unique()}";

        var created = await service.CreateAsync(Request(name));
        Assert.True(created.Succeeded, created.Error);

        var deleted = await service.DeleteAsync(created.Value);
        Assert.True(deleted.Succeeded, deleted.Error);

        Assert.Null(await service.GetAsync(created.Value));

        // The unique indexes are filtered on IsDeleted, so the name and the
        // address both come back into circulation.
        var reused = await service.CreateAsync(Request(name));
        Assert.True(reused.Succeeded, reused.Error);
    }

    [Fact]
    public async Task A_brand_that_still_has_products_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var created = await service.CreateAsync(Request($"Occupied {suffix}"));
        Assert.True(created.Succeeded, created.Error);

        var unit = await db.UnitsOfMeasure.FirstAsync(u => u.Code == "PC");

        db.Products.Add(new Product
        {
            Code = $"OCC-{suffix}",
            Name = $"Occupied product {suffix}",
            Slug = $"occupied-product-{suffix}",
            BrandId = created.Value,
            UnitOfMeasureId = unit.Id,
        });
        await db.SaveChangesAsync();

        var deleted = await service.DeleteAsync(created.Value);

        Assert.False(deleted.Succeeded);
        Assert.Contains("deactivate", deleted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_inactive_brand_still_appears_in_the_options_of_a_product_that_uses_it()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var name = $"Retired {Unique()}";
        var created = await service.CreateAsync(Request(name));
        Assert.True(created.Succeeded, created.Error);

        var detail = await service.GetAsync(created.Value);

        var deactivate = Request(detail!.Name, detail.Slug);
        deactivate.IsActive = false;

        var updated = await service.UpdateAsync(created.Value, deactivate);
        Assert.True(updated.Succeeded, updated.Error);

        var withoutIt = await service.GetOptionsAsync();
        var withIt = await service.GetOptionsAsync(created.Value);

        // Omitting it would make saving an existing product silently reassign
        // it to whichever brand happened to be first in the list.
        Assert.DoesNotContain(withoutIt, o => o.Id == created.Value);
        Assert.Contains(withIt, o => o.Id == created.Value);
    }

    [Fact]
    public async Task The_status_filter_separates_active_from_inactive()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BrandAdminService>();

        var suffix = Unique();

        var active = Request($"Filter Active {suffix}");
        var inactive = Request($"Filter Inactive {suffix}");
        inactive.IsActive = false;

        Assert.True((await service.CreateAsync(active)).Succeeded);
        Assert.True((await service.CreateAsync(inactive)).Succeeded);

        var activeRows = await service.ListAsync(suffix, 0, 50, null, false, StatusFilter.Active);
        var inactiveRows = await service.ListAsync(suffix, 0, 50, null, false, StatusFilter.Inactive);

        Assert.Single(activeRows.Rows);
        Assert.Single(inactiveRows.Rows);
        Assert.Equal($"Filter Active {suffix}", activeRows.Rows[0].Name);
        Assert.Equal($"Filter Inactive {suffix}", inactiveRows.Rows[0].Name);
    }
}
