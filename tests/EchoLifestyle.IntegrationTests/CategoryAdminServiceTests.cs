using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class CategoryAdminServiceTests
{
    private readonly DatabaseFixture _fixture;

    public CategoryAdminServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static SaveCategoryRequest Request(string name, long? parentId = null) => new()
    {
        Name = name,
        ParentId = parentId,
        IsActive = true,
        ShowInMenu = true,
    };

    private static async Task<Category> LoadAsync(IServiceProvider services, long id) =>
        await services.GetRequiredService<EchoDbContext>()
            .Categories
            .AsNoTracking()
            .SingleAsync(c => c.Id == id);

    [Fact]
    public async Task A_root_category_gets_a_path_containing_only_itself()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var created = await service.CreateAsync(Request($"Skincare {Unique()}"));
        Assert.True(created.Succeeded, created.Error);

        var row = await LoadAsync(scope.ServiceProvider, created.Value);

        Assert.Equal($"/{row.Id}/", row.Path);
        Assert.Equal(0, row.Depth);
    }

    [Fact]
    public async Task A_child_inherits_its_parents_path()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var parent = await service.CreateAsync(Request($"Haircare {Unique()}"));
        var child = await service.CreateAsync(Request("Shampoo", parent.Value));

        Assert.True(child.Succeeded, child.Error);

        var parentRow = await LoadAsync(scope.ServiceProvider, parent.Value);
        var childRow = await LoadAsync(scope.ServiceProvider, child.Value);

        Assert.Equal($"{parentRow.Path}{childRow.Id}/", childRow.Path);
        Assert.Equal(1, childRow.Depth);
    }

    [Fact]
    public async Task Moving_a_category_rewrites_the_paths_of_everything_beneath_it()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var oldHome = await service.CreateAsync(Request($"Old home {suffix}"));
        var newHome = await service.CreateAsync(Request($"New home {suffix}"));
        var branch = await service.CreateAsync(Request("Moisturisers", oldHome.Value));
        var leaf = await service.CreateAsync(Request("Night creams", branch.Value));

        Assert.True(leaf.Succeeded, leaf.Error);

        var move = Request("Moisturisers", newHome.Value);
        var moved = await service.UpdateAsync(branch.Value, move);
        Assert.True(moved.Succeeded, moved.Error);

        var newHomeRow = await LoadAsync(scope.ServiceProvider, newHome.Value);
        var branchRow = await LoadAsync(scope.ServiceProvider, branch.Value);
        var leafRow = await LoadAsync(scope.ServiceProvider, leaf.Value);

        // The grandchild has to follow. A stale path here would silently drop
        // the leaf out of every "everything under New home" listing.
        Assert.Equal($"{newHomeRow.Path}{branchRow.Id}/", branchRow.Path);
        Assert.Equal($"{branchRow.Path}{leafRow.Id}/", leafRow.Path);
        Assert.Equal(1, branchRow.Depth);
        Assert.Equal(2, leafRow.Depth);
    }

    [Fact]
    public async Task Promoting_a_child_to_the_top_level_shifts_its_subtrees_depth()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var root = await service.CreateAsync(Request($"Temporary root {suffix}"));
        var child = await service.CreateAsync(Request($"Promoted {suffix}", root.Value));
        var grandchild = await service.CreateAsync(Request("Deep", child.Value));

        var promote = Request($"Promoted {suffix}");
        var moved = await service.UpdateAsync(child.Value, promote);
        Assert.True(moved.Succeeded, moved.Error);

        var childRow = await LoadAsync(scope.ServiceProvider, child.Value);
        var grandchildRow = await LoadAsync(scope.ServiceProvider, grandchild.Value);

        Assert.Null(childRow.ParentId);
        Assert.Equal(0, childRow.Depth);
        Assert.Equal($"/{childRow.Id}/", childRow.Path);
        Assert.Equal(1, grandchildRow.Depth);
        Assert.Equal($"{childRow.Path}{grandchildRow.Id}/", grandchildRow.Path);
    }

    [Fact]
    public async Task A_category_cannot_be_moved_under_its_own_descendant()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var parent = await service.CreateAsync(Request($"Cycle parent {suffix}"));
        var child = await service.CreateAsync(Request("Cycle child", parent.Value));

        var move = Request($"Cycle parent {suffix}", child.Value);
        var result = await service.UpdateAsync(parent.Value, move);

        // A cycle would detach the whole subtree from the root and give the
        // path rewrite nothing to terminate on.
        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveCategoryRequest.ParentId), result.Field);
    }

    [Fact]
    public async Task A_category_cannot_be_its_own_parent()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var created = await service.CreateAsync(Request($"Self {Unique()}"));

        var move = Request($"Self {Unique()}", created.Value);
        var result = await service.UpdateAsync(created.Value, move);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveCategoryRequest.ParentId), result.Field);
    }

    [Fact]
    public async Task The_tree_stops_at_three_levels()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var level0 = await service.CreateAsync(Request($"L0 {suffix}"));
        var level1 = await service.CreateAsync(Request("L1", level0.Value));
        var level2 = await service.CreateAsync(Request("L2", level1.Value));

        Assert.True(level2.Succeeded, level2.Error);

        var level3 = await service.CreateAsync(Request("L3", level2.Value));

        Assert.False(level3.Succeeded);
        Assert.Equal(nameof(SaveCategoryRequest.ParentId), level3.Field);
    }

    [Fact]
    public async Task A_move_is_refused_when_it_would_push_a_descendant_past_the_limit()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        // A two-level subtree that currently sits at the root.
        var subtreeRoot = await service.CreateAsync(Request($"Subtree {suffix}"));
        var subtreeChild = await service.CreateAsync(Request("Subtree child", subtreeRoot.Value));
        Assert.True((await service.CreateAsync(Request("Subtree leaf", subtreeChild.Value))).Succeeded);

        // Moving it one level down would put its leaf at depth 3.
        var newParent = await service.CreateAsync(Request($"New parent {suffix}"));

        var move = Request($"Subtree {suffix}", newParent.Value);
        var result = await service.UpdateAsync(subtreeRoot.Value, move);

        // The node itself would fit at depth 1 - it is the leaf that would not,
        // which is exactly the case a naive depth check misses.
        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveCategoryRequest.ParentId), result.Field);
    }

    [Fact]
    public async Task Siblings_cannot_share_a_slug_but_cousins_can()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var skincare = await service.CreateAsync(Request($"Skincare {suffix}"));
        var haircare = await service.CreateAsync(Request($"Haircare {suffix}"));

        var underSkincare = await service.CreateAsync(Request("Cleansers", skincare.Value));
        var underHaircare = await service.CreateAsync(Request("Cleansers", haircare.Value));

        Assert.True(underSkincare.Succeeded, underSkincare.Error);
        Assert.True(underHaircare.Succeeded, underHaircare.Error);

        var first = await LoadAsync(scope.ServiceProvider, underSkincare.Value);
        var second = await LoadAsync(scope.ServiceProvider, underHaircare.Value);

        // Both keep the clean address - forcing global uniqueness would have
        // produced "cleansers-2" for no reason a shopper could see.
        Assert.Equal("cleansers", first.Slug);
        Assert.Equal("cleansers", second.Slug);

        var duplicate = await service.CreateAsync(new SaveCategoryRequest
        {
            Name = "Cleansers Again",
            ParentId = skincare.Value,
            Slug = "cleansers",
        });

        Assert.False(duplicate.Succeeded);
        Assert.Equal(nameof(SaveCategoryRequest.Slug), duplicate.Field);
    }

    [Fact]
    public async Task A_category_with_children_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var parent = await service.CreateAsync(Request($"Occupied {Unique()}"));
        Assert.True((await service.CreateAsync(Request("A child", parent.Value))).Succeeded);

        var deleted = await service.DeleteAsync(parent.Value);

        // Cascading would take a whole branch of the merchandising tree with
        // one click.
        Assert.False(deleted.Succeeded);
        Assert.Contains("sub-categor", deleted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_category_holding_products_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();
        var brands = scope.ServiceProvider.GetRequiredService<
            Application.Catalog.Brands.BrandAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();

        var category = await service.CreateAsync(Request($"Holding {suffix}"));

        var brand = await brands.CreateAsync(new Application.Catalog.Brands.SaveBrandRequest
        {
            Name = $"Category test brand {suffix}",
            IsActive = true,
        });

        var unit = await db.UnitsOfMeasure.FirstAsync(u => u.Code == "PC");

        var product = new Product
        {
            Code = $"CAT-{suffix}",
            Name = $"Categorised {suffix}",
            Slug = $"categorised-{suffix}",
            BrandId = brand.Value,
            UnitOfMeasureId = unit.Id,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = category.Value,
            IsPrimary = true,
        });
        await db.SaveChangesAsync();

        var deleted = await service.DeleteAsync(category.Value);

        Assert.False(deleted.Succeeded);
        Assert.Contains("deactivate", deleted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_category_can_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var created = await service.CreateAsync(Request($"Transient {Unique()}"));

        var deleted = await service.DeleteAsync(created.Value);

        Assert.True(deleted.Succeeded, deleted.Error);
        Assert.Null(await service.GetAsync(created.Value));
    }

    [Fact]
    public async Task The_parent_picker_excludes_the_edited_node_and_its_own_subtree()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var root = await service.CreateAsync(Request($"Picker root {suffix}"));
        var child = await service.CreateAsync(Request("Picker child", root.Value));

        var options = await service.GetParentOptionsAsync(root.Value);

        // Offering either would let the user create a cycle and then be told
        // off for it. Better not to offer.
        Assert.DoesNotContain(options, o => o.Id == root.Value);
        Assert.DoesNotContain(options, o => o.Id == child.Value);
    }

    [Fact]
    public async Task The_parent_picker_excludes_nodes_that_are_already_at_maximum_depth()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var level0 = await service.CreateAsync(Request($"Deep root {suffix}"));
        var level1 = await service.CreateAsync(Request("Deep middle", level0.Value));
        var level2 = await service.CreateAsync(Request("Deep leaf", level1.Value));

        var options = await service.GetParentOptionsAsync();

        Assert.Contains(options, o => o.Id == level1.Value);
        Assert.DoesNotContain(options, o => o.Id == level2.Value);
    }

    [Fact]
    public async Task The_breadcrumb_names_every_ancestor_in_order()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var root = await service.CreateAsync(Request($"Crumb root {suffix}"));
        var middle = await service.CreateAsync(Request("Crumb middle", root.Value));
        var leaf = await service.CreateAsync(Request("Crumb leaf", middle.Value));

        var detail = await service.GetAsync(leaf.Value);

        Assert.Equal($"Crumb root {suffix} > Crumb middle > Crumb leaf", detail!.Breadcrumb);
    }

    [Fact]
    public async Task The_tree_lists_children_directly_after_their_parent()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var root = await service.CreateAsync(Request($"Order root {suffix}"));

        var second = Request("Second", root.Value);
        second.DisplayOrder = 2;
        var first = Request("First", root.Value);
        first.DisplayOrder = 1;

        // Created out of order on purpose - display order, not insertion order,
        // decides the result.
        var secondId = (await service.CreateAsync(second)).Value;
        var firstId = (await service.CreateAsync(first)).Value;

        var tree = await service.GetTreeAsync(search: suffix);

        var ids = tree.Select(n => n.Id).ToList();
        var rootIndex = ids.IndexOf(root.Value);

        Assert.Equal(root.Value, ids[rootIndex]);
        Assert.Equal(firstId, ids[rootIndex + 1]);
        Assert.Equal(secondId, ids[rootIndex + 2]);
    }
}
