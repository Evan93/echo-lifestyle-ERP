using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class ProductAdminServiceTests
{
    private readonly DatabaseFixture _fixture;

    public ProductAdminServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// A brand and a category of its own per test, so nothing here depends on
    /// what another test class happened to leave behind.
    /// </summary>
    private static async Task<(long BrandId, long CategoryId)> ScaffoldAsync(
        IServiceProvider services,
        string suffix)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();

        var brand = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Product test brand {suffix}",
            IsActive = true,
        });

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Product test category {suffix}",
            IsActive = true,
        });

        return (brand.Value, category.Value);
    }

    private static async Task<long> QuickCreateAsync(
        IServiceProvider services,
        string suffix,
        decimal? price = 500m)
    {
        var (brandId, categoryId) = await ScaffoldAsync(services, suffix);
        var products = services.GetRequiredService<ProductAdminService>();

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Test product {suffix}",
            BrandId = brandId,
            CategoryId = categoryId,
            Price = price,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        return created.Value;
    }

    private static SaveOptionsRequest Options(params (string Name, string Values)[] rows) => new()
    {
        Options = rows.Select(r => new OptionInput { Name = r.Name, Values = r.Values }).ToList(),
    };

    // -----------------------------------------------------------------------
    // Quick create
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Quick_create_produces_a_complete_product_with_one_priced_default_variant()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var id = await QuickCreateAsync(scope.ServiceProvider, suffix, 750m);

        var detail = await products.GetAsync(id);

        Assert.NotNull(detail);
        Assert.StartsWith("P-", detail!.Code);
        Assert.Equal($"test-product-{suffix}", detail.Slug);
        Assert.Empty(detail.Options);

        // One variant, flagged default, priced, and not published. A quick
        // create that produced a half-formed product would just move the work
        // somewhere less visible.
        var variant = Assert.Single(detail.Variants);
        Assert.True(variant.IsDefault);
        Assert.True(variant.IsActive);
        Assert.Equal(750m, variant.CurrentPrice);
        Assert.False(detail.IsPublished);
    }

    [Fact]
    public async Task Quick_create_generates_sequential_codes_when_none_is_given()
    {
        await using var scope = _fixture.CreateScope();

        var first = await QuickCreateAsync(scope.ServiceProvider, Unique());
        var second = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var firstCode = (await products.GetAsync(first))!.Code;
        var secondCode = (await products.GetAsync(second))!.Code;

        Assert.NotEqual(firstCode, secondCode);
        Assert.Matches(@"^P-\d{6}$", firstCode);
        Assert.Matches(@"^P-\d{6}$", secondCode);
    }

    [Fact]
    public async Task Tracking_expiry_forces_batch_tracking_on()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var (brandId, categoryId) = await ScaffoldAsync(scope.ServiceProvider, suffix);

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Expiring {suffix}",
            BrandId = brandId,
            CategoryId = categoryId,
            IsExpiryTracked = true,
            IsBatchTracked = false,
            IsActive = true,
        });

        var detail = await products.GetAsync(created.Value);

        // An expiry date with no batch to hang it on could never be traced back
        // to a delivery.
        Assert.True(detail!.IsExpiryTracked);
        Assert.True(detail.IsBatchTracked);
    }

    // -----------------------------------------------------------------------
    // The variant matrix
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Adding_options_builds_every_combination()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var result = await products.SaveOptionsAsync(
            id, Options(("Size", "30ml, 50ml"), ("Shade", "Ruby Red #C21807, Coral")));

        Assert.True(result.Succeeded, result.Error);

        var detail = await products.GetAsync(id);

        Assert.Equal(4, detail!.Variants.Count);
        Assert.Equal(
            ["30ml / Ruby Red", "30ml / Coral", "50ml / Ruby Red", "50ml / Coral"],
            detail.Variants.Select(v => v.VariantName).ToArray());

        // Exactly one default, always.
        Assert.Single(detail.Variants, v => v.IsDefault);

        var shade = detail.Options.Single(o => o.Name == "Shade");
        Assert.Equal("#C21807", shade.Values.Single(v => v.Value == "Ruby Red").SwatchHex);
        Assert.Null(shade.Values.Single(v => v.Value == "Coral").SwatchHex);
    }

    [Fact]
    public async Task The_quick_created_variant_is_adopted_rather_than_retired()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), 900m);

        var before = (await products.GetAsync(id))!.Variants.Single();

        var result = await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));
        Assert.True(result.Succeeded, result.Error);

        var detail = await products.GetAsync(id);
        var adopted = detail!.Variants.Single(v => v.Id == before.Id);

        // The SKU is on a shelf label and the price was already entered. Both
        // survive becoming the first size.
        Assert.Equal(before.Sku, adopted.Sku);
        Assert.Equal(900m, adopted.CurrentPrice);
        Assert.Equal("30ml", adopted.VariantName);
        Assert.Equal(2, detail.Variants.Count);
    }

    [Fact]
    public async Task Adding_a_value_keeps_existing_variants_untouched()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));

        var before = (await products.GetAsync(id))!.Variants
            .ToDictionary(v => v.VariantName, v => v.Sku);

        var result = await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml, 100ml")));
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.Created);
        Assert.Equal(2, result.Value.Retained);

        var after = (await products.GetAsync(id))!.Variants
            .ToDictionary(v => v.VariantName, v => v.Sku);

        // Matching is by the text of the option values, so the two that already
        // existed keep their identity rather than being rebuilt.
        Assert.Equal(before["30ml"], after["30ml"]);
        Assert.Equal(before["50ml"], after["50ml"]);
        Assert.Equal(3, after.Count);
    }

    [Fact]
    public async Task Reordering_options_does_not_rebuild_the_variants()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml"), ("Shade", "Red, Blue")));

        var before = (await products.GetAsync(id))!.Variants.Select(v => v.Sku).OrderBy(s => s).ToList();

        var result = await products.SaveOptionsAsync(
            id, Options(("Shade", "Red, Blue"), ("Size", "30ml, 50ml")));

        Assert.True(result.Succeeded, result.Error);

        // Signatures are order-independent, so swapping the axes around is a
        // presentation change and nothing more.
        Assert.Equal(0, result.Value!.Created);
        Assert.Equal(4, result.Value.Retained);

        var after = (await products.GetAsync(id))!.Variants.Select(v => v.Sku).OrderBy(s => s).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_variant_with_price_history_is_retired_rather_than_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));

        var detail = await products.GetAsync(id);
        var priced = detail!.Variants.Single(v => v.VariantName == "50ml");

        // Give the 50ml a price, then remove that size entirely.
        var save = await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = detail.Variants
                .Select(v => new VariantInput
                {
                    Id = v.Id,
                    Sku = v.Sku,
                    Price = v.Id == priced.Id ? 1200m : v.CurrentPrice,
                    IsActive = true,
                })
                .ToList(),
        });

        Assert.True(save.Succeeded, save.Error);

        var result = await products.SaveOptionsAsync(id, Options(("Size", "30ml")));
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value!.Retired);
        Assert.Equal(0, result.Value.Removed);

        var after = await products.GetAsync(id);
        var retired = after!.Variants.Single(v => v.Id == priced.Id);

        Assert.False(retired.IsActive);
        Assert.True(retired.IsRetired);

        // The name is frozen rather than derived, which is the whole reason it
        // is stored: the option value it referred to no longer exists.
        Assert.Equal("50ml", retired.VariantName);
    }

    [Fact]
    public async Task A_variant_with_no_history_is_removed_outright()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), price: null);

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml, 100ml")));

        var result = await products.SaveOptionsAsync(id, Options(("Size", "30ml")));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, result.Value!.Removed);
        Assert.Equal(0, result.Value.Retired);

        var detail = await products.GetAsync(id);
        Assert.Single(detail!.Variants);
    }

    [Fact]
    public async Task Clearing_every_option_collapses_back_to_one_default_variant()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), 650m);

        var originalSku = (await products.GetAsync(id))!.Variants.Single().Sku;

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));
        var result = await products.SaveOptionsAsync(id, new SaveOptionsRequest());

        Assert.True(result.Succeeded, result.Error);

        var detail = await products.GetAsync(id);

        Assert.Empty(detail!.Options);

        var variant = detail.Variants.Single(v => v.IsActive);
        Assert.True(variant.IsDefault);
        Assert.Equal(ProductAdminService.DefaultVariantName, variant.VariantName);

        // The default variant is adopted back, so the original SKU and its
        // price survive the round trip.
        Assert.Equal(originalSku, variant.Sku);
        Assert.Equal(650m, variant.CurrentPrice);
    }

    [Fact]
    public async Task Rebuilding_twice_with_retired_variants_present_does_not_fail()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml, 100ml")));

        var detail = await products.GetAsync(id);

        // Price everything, so removals become retirements.
        await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = detail!.Variants
                .Select(v => new VariantInput { Id = v.Id, Sku = v.Sku, Price = 100m, IsActive = true })
                .ToList(),
        });

        var first = await products.SaveOptionsAsync(id, Options(("Size", "30ml")));
        Assert.True(first.Succeeded, first.Error);
        Assert.Equal(2, first.Value!.Retired);

        // Two retired variants now share the empty signature. A dictionary
        // built naively from those would have thrown on the duplicate key.
        var second = await products.SaveOptionsAsync(id, Options(("Size", "30ml, 200ml")));

        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(1, second.Value!.Created);
    }

    [Fact]
    public async Task Generated_skus_within_one_rebuild_are_distinct()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var result = await products.SaveOptionsAsync(
            id, Options(("Size", "30ml, 50ml, 100ml"), ("Shade", "Red, Blue, Green")));

        Assert.True(result.Succeeded, result.Error);

        var detail = await products.GetAsync(id);
        var skus = detail!.Variants.Select(v => v.Sku).ToList();

        // Nine variants created in one pass, none of them saved until the end -
        // generating SKUs by querying the database each time would have handed
        // out the same one nine times.
        Assert.Equal(9, skus.Count);
        Assert.Equal(9, skus.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task No_variant_is_ever_claimed_by_two_combinations()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        // The shape that broke it: some combinations match an existing variant
        // while another has no match at all, so the matching path and the
        // adoption path both run in the same rebuild.
        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));
        var grow = await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml, 100ml")));

        Assert.True(grow.Succeeded, grow.Error);

        var variantIds = await db.ProductVariants
            .Where(v => v.ProductId == id)
            .Select(v => v.Id)
            .ToListAsync();

        var links = await db.ProductVariantOptionValues
            .Where(l => variantIds.Contains(l.ProductVariantId))
            .Select(l => new { l.ProductVariantId, l.ProductOptionId })
            .ToListAsync();

        // One row per (variant, option). Two combinations sharing a variant
        // would put two here and the unique index would reject the save - which
        // is exactly how this surfaced.
        Assert.Equal(links.Count, links.Distinct().Count());
        Assert.Equal(3, links.Count);
    }

    [Fact]
    public async Task An_option_with_no_values_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var result = await products.SaveOptionsAsync(id, Options(("Size", "   ")));

        Assert.False(result.Succeeded);
        Assert.Contains("at least one value", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_blank_row_is_treated_as_an_unused_axis()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var result = await products.SaveOptionsAsync(
            id, Options(("Size", "30ml, 50ml"), ("", ""), ("  ", null!)));

        Assert.True(result.Succeeded, result.Error);

        var detail = await products.GetAsync(id);
        Assert.Single(detail!.Options);
    }

    [Fact]
    public async Task Duplicate_values_within_an_option_are_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var result = await products.SaveOptionsAsync(id, Options(("Size", "30ml, 30ML")));

        Assert.False(result.Succeeded);
        Assert.Contains("twice", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_combination_count_beyond_the_limit_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var many = string.Join(", ", Enumerable.Range(1, 11).Select(n => $"V{n}"));

        var result = await products.SaveOptionsAsync(
            id, Options(("A", many), ("B", many)));

        // 121 combinations. Refusing beats rendering a table nobody can fill in.
        Assert.False(result.Succeeded);
        Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Variants and pricing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Changing_a_price_closes_the_old_row_and_opens_a_new_one()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), 500m);
        var variant = (await products.GetAsync(id))!.Variants.Single();

        var result = await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = [new VariantInput { Id = variant.Id, Sku = variant.Sku, Price = 620m, IsActive = true }],
        });

        Assert.True(result.Succeeded, result.Error);

        var history = await db.PriceListItems
            .Where(i => i.ProductVariantId == variant.Id)
            .OrderBy(i => i.EffectiveFromUtc)
            .ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal(500m, history[0].UnitPrice);
        Assert.NotNull(history[0].EffectiveToUtc);
        Assert.Equal(620m, history[1].UnitPrice);
        Assert.Null(history[1].EffectiveToUtc);
    }

    [Fact]
    public async Task Saving_the_same_price_again_does_not_add_a_history_row()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), 500m);
        var variant = (await products.GetAsync(id))!.Variants.Single();

        await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = [new VariantInput { Id = variant.Id, Sku = variant.Sku, Price = 500m, IsActive = true }],
        });

        // Re-saving an unchanged form is the most common thing anyone does. It
        // must not fill the price history with identical rows.
        Assert.Equal(1, await db.PriceListItems.CountAsync(i => i.ProductVariantId == variant.Id));
    }

    [Fact]
    public async Task Prices_posted_without_the_permission_are_ignored()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), 500m);
        var variant = (await products.GetAsync(id))!.Variants.Single();

        var result = await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = false,
            Variants =
            [
                new VariantInput { Id = variant.Id, Sku = variant.Sku, Price = 1m, Barcode = "5901234123457", IsActive = true },
            ],
        });

        Assert.True(result.Succeeded, result.Error);

        var after = (await products.GetAsync(id))!.Variants.Single();

        // The rest of the row still saves - only the price is refused.
        Assert.Equal(500m, after.CurrentPrice);
        Assert.Equal("5901234123457", after.Barcode);
    }

    [Fact]
    public async Task An_sku_used_by_another_product_is_refused_by_name()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var firstId = await QuickCreateAsync(scope.ServiceProvider, Unique());
        var secondId = await QuickCreateAsync(scope.ServiceProvider, Unique());

        var taken = (await products.GetAsync(firstId))!.Variants.Single().Sku;
        var target = (await products.GetAsync(secondId))!.Variants.Single();

        var result = await products.SaveVariantsAsync(secondId, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = [new VariantInput { Id = target.Id, Sku = taken, IsActive = true }],
        });

        Assert.False(result.Succeeded);
        Assert.Contains(taken, result.Error);
    }

    [Fact]
    public async Task The_same_sku_twice_in_one_form_is_caught_before_the_database_sees_it()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());
        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));

        var detail = await products.GetAsync(id);

        var result = await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = detail!.Variants
                .Select(v => new VariantInput { Id = v.Id, Sku = "SAME-SKU", IsActive = true })
                .ToList(),
        });

        // Reaching the unique index here would surface as an unexplained
        // constraint violation rather than as a sentence about the form.
        Assert.False(result.Succeeded);
        Assert.Contains("twice in this form", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deactivating_every_variant_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());
        var variant = (await products.GetAsync(id))!.Variants.Single();

        var result = await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = [new VariantInput { Id = variant.Id, Sku = variant.Sku, IsActive = false }],
        });

        Assert.False(result.Succeeded);
        Assert.Contains("at least one variant", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Publishing and deletion
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_unpriced_product_cannot_be_published()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), price: null);

        var result = await products.SetPublishedAsync(id, true);

        // Published with no price, the storefront shows it as free or as
        // nothing at all, depending on the template.
        Assert.False(result.Succeeded);
        Assert.Contains("price", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_inactive_product_cannot_be_published()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var (brandId, categoryId) = await ScaffoldAsync(scope.ServiceProvider, suffix);

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Dormant {suffix}",
            BrandId = brandId,
            CategoryId = categoryId,
            Price = 100m,
            IsActive = false,
        });

        var result = await products.SetPublishedAsync(created.Value, true);

        Assert.False(result.Succeeded);
        Assert.Contains("activate", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deactivating_a_published_product_also_takes_it_off_the_storefront()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var id = await QuickCreateAsync(scope.ServiceProvider, suffix, 400m);

        Assert.True((await products.SetPublishedAsync(id, true)).Succeeded);

        var detail = await products.GetAsync(id);

        var result = await products.UpdateAsync(id, new SaveProductRequest
        {
            Code = detail!.Code,
            Name = detail.Name,
            Slug = detail.Slug,
            BrandId = detail.BrandId,
            UnitOfMeasureId = detail.UnitOfMeasureId,
            CategoryIds = detail.CategoryIds,
            PrimaryCategoryId = detail.PrimaryCategoryId,
            IsActive = false,
        });

        Assert.True(result.Succeeded, result.Error);

        var after = await products.GetAsync(id);

        // Leaving it published would advertise something the ERP refuses to sell.
        Assert.False(after!.IsActive);
        Assert.False(after.IsPublished);
    }

    [Fact]
    public async Task A_product_with_no_category_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique());
        var detail = await products.GetAsync(id);

        var result = await products.UpdateAsync(id, new SaveProductRequest
        {
            Code = detail!.Code,
            Name = detail.Name,
            Slug = detail.Slug,
            BrandId = detail.BrandId,
            UnitOfMeasureId = detail.UnitOfMeasureId,
            CategoryIds = [],
            IsActive = true,
        });

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveProductRequest.CategoryIds), result.Field);
    }

    [Fact]
    public async Task Moving_the_primary_category_leaves_exactly_one_primary()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var categories = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var id = await QuickCreateAsync(scope.ServiceProvider, suffix);
        var detail = await products.GetAsync(id);

        var second = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Second category {suffix}",
            IsActive = true,
        });

        var result = await products.UpdateAsync(id, new SaveProductRequest
        {
            Code = detail!.Code,
            Name = detail.Name,
            Slug = detail.Slug,
            BrandId = detail.BrandId,
            UnitOfMeasureId = detail.UnitOfMeasureId,
            CategoryIds = [.. detail.CategoryIds, second.Value],
            PrimaryCategoryId = second.Value,
            IsActive = true,
        });

        Assert.True(result.Succeeded, result.Error);

        // The filtered unique index would have rejected two primaries, so this
        // passing is what proves the flag is cleared before the new one is set.
        var links = await db.ProductCategories.Where(pc => pc.ProductId == id).ToListAsync();

        Assert.Equal(2, links.Count);
        Assert.Single(links, l => l.IsPrimary);
        Assert.Equal(second.Value, links.Single(l => l.IsPrimary).CategoryId);
    }

    [Fact]
    public async Task A_priced_product_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), 300m);

        var result = await products.DeleteAsync(id);

        Assert.False(result.Succeeded);
        Assert.Contains("deactivate", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unpriced_product_can_be_deleted_but_keeps_its_sku_reserved()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await QuickCreateAsync(scope.ServiceProvider, Unique(), price: null);
        var sku = (await products.GetAsync(id))!.Variants.Single().Sku;

        var deleted = await products.DeleteAsync(id);
        Assert.True(deleted.Succeeded, deleted.Error);
        Assert.Null(await products.GetAsync(id));

        var otherId = await QuickCreateAsync(scope.ServiceProvider, Unique(), price: null);
        var other = (await products.GetAsync(otherId))!.Variants.Single();

        var reuse = await products.SaveVariantsAsync(otherId, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = [new VariantInput { Id = other.Id, Sku = sku, IsActive = true }],
        });

        // An SKU that has ever held stock must never mean two different things,
        // and the message says why rather than just refusing.
        Assert.False(reuse.Succeeded);
        Assert.Contains("deleted", reuse.Error, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Listing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_list_shows_a_price_range_across_variants()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var id = await QuickCreateAsync(scope.ServiceProvider, suffix);

        await products.SaveOptionsAsync(id, Options(("Size", "30ml, 50ml")));
        var detail = await products.GetAsync(id);

        await products.SaveVariantsAsync(id, new SaveVariantsRequest
        {
            MayEditPrices = true,
            Variants = detail!.Variants
                .Select((v, i) => new VariantInput
                {
                    Id = v.Id,
                    Sku = v.Sku,
                    Price = i == 0 ? 400m : 700m,
                    IsActive = true,
                })
                .ToList(),
        });

        var page = await products.ListAsync(
            suffix, 0, 50, null, false, StatusFilter.Active, null, null);

        var row = Assert.Single(page.Rows);

        Assert.Equal(400m, row.PriceFrom);
        Assert.Equal(700m, row.PriceTo);
        Assert.Equal(2, row.VariantCount);
    }

    [Fact]
    public async Task Filtering_by_a_parent_category_includes_its_descendants()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var brands = scope.ServiceProvider.GetRequiredService<BrandAdminService>();
        var categories = scope.ServiceProvider.GetRequiredService<CategoryAdminService>();

        var suffix = Unique();

        var parent = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Parent {suffix}",
            IsActive = true,
        });

        var child = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = "Child",
            ParentId = parent.Value,
            IsActive = true,
        });

        var brand = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Descendant brand {suffix}",
            IsActive = true,
        });

        await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Deep product {suffix}",
            BrandId = brand.Value,
            CategoryId = child.Value,
            Price = 100m,
            IsActive = true,
        });

        var page = await products.ListAsync(
            suffix, 0, 50, null, false, StatusFilter.Active, null, parent.Value);

        // Filtering by Skincare has to return the night creams too - which is
        // what the materialised path exists for.
        Assert.Single(page.Rows);
    }
}
