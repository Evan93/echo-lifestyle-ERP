using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// The catalogue guarantees that live in the database.
///
/// Each of these is a filtered unique index. Filtered indexes are the reason
/// these tests run against real SQL Server: an in-memory provider would accept
/// every one of the rows below and report success.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CatalogConstraintsTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private long _brandId;
    private long _unitId;

    public CatalogConstraintsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];

        var brand = new Brand { Name = $"Constraint Brand {suffix}", Slug = $"constraint-brand-{suffix}" };
        var unit = new UnitOfMeasure { Code = $"U{suffix[..4]}", Name = "Constraint Unit" };

        db.AddRange(brand, unit);
        await db.SaveChangesAsync();

        _brandId = brand.Id;
        _unitId = unit.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Product NewProduct(string suffix) => new()
    {
        Code = $"P-{suffix}",
        Name = $"Product {suffix}",
        Slug = $"product-{suffix}",
        BrandId = _brandId,
        UnitOfMeasureId = _unitId,
    };

    private static string Suffix() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task A_product_cannot_have_two_default_variants()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductVariants.Add(new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"SKU-{suffix}-A",
            VariantName = "Default",
            IsDefault = true,
        });
        await db.SaveChangesAsync();

        db.ProductVariants.Add(new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"SKU-{suffix}-B",
            VariantName = "Also default",
            IsDefault = true,
        });

        // Two defaults would make "the variant for this product" depend on row
        // order, which quick entry and every import rely on being single.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Skus_are_unique_across_the_whole_catalogue()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var sku = $"SHARED-{Suffix()}";

        var first = NewProduct(Suffix());
        var second = NewProduct(Suffix());
        db.AddRange(first, second);
        await db.SaveChangesAsync();

        db.ProductVariants.Add(new ProductVariant
        {
            ProductId = first.Id,
            Sku = sku,
            VariantName = "First",
            IsDefault = true,
        });
        await db.SaveChangesAsync();

        db.ProductVariants.Add(new ProductVariant
        {
            ProductId = second.Id,
            Sku = sku,
            VariantName = "Second",
            IsDefault = true,
        });

        // Not scoped to the product: an SKU is what a barcode label, a purchase
        // line and a stock count all agree on.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Many_variants_may_have_no_barcode_but_a_barcode_cannot_be_shared()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Two barcode-less variants: a plain unique index would reject the
        // second, which is why the index is filtered on NOT NULL.
        db.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, Sku = $"NB1-{suffix}", VariantName = "One", IsDefault = true },
            new ProductVariant { ProductId = product.Id, Sku = $"NB2-{suffix}", VariantName = "Two" });

        await db.SaveChangesAsync();

        var barcode = $"BC{suffix}";

        var withBarcode = await db.ProductVariants.FirstAsync(v => v.Sku == $"NB1-{suffix}");
        withBarcode.Barcode = barcode;
        await db.SaveChangesAsync();

        var second = await db.ProductVariants.FirstAsync(v => v.Sku == $"NB2-{suffix}");
        second.Barcode = barcode;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_variant_cannot_hold_two_values_for_the_same_option()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var option = new ProductOption { ProductId = product.Id, Name = "Size" };
        db.ProductOptions.Add(option);
        await db.SaveChangesAsync();

        var small = new ProductOptionValue { ProductOptionId = option.Id, Value = "30ml" };
        var large = new ProductOptionValue { ProductOptionId = option.Id, Value = "50ml" };
        db.AddRange(small, large);

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"OPT-{suffix}",
            VariantName = "30ml",
            IsDefault = true,
        };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        db.ProductVariantOptionValues.Add(new ProductVariantOptionValue
        {
            ProductVariantId = variant.Id,
            ProductOptionId = option.Id,
            ProductOptionValueId = small.Id,
        });
        await db.SaveChangesAsync();

        db.ProductVariantOptionValues.Add(new ProductVariantOptionValue
        {
            ProductVariantId = variant.Id,
            ProductOptionId = option.Id,
            ProductOptionValueId = large.Id,
        });

        // A variant that is both 30ml and 50ml would appear twice in every
        // size-filtered listing.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_price_may_be_open_for_a_variant_on_a_list()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"PRICE-{suffix}",
            VariantName = "Default",
            IsDefault = true,
        };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var priceList = await db.PriceLists.FirstAsync(p => p.IsDefault);

        db.PriceListItems.Add(new PriceListItem
        {
            PriceListId = priceList.Id,
            ProductVariantId = variant.Id,
            UnitPrice = 950m,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-10),
        });
        await db.SaveChangesAsync();

        db.PriceListItems.Add(new PriceListItem
        {
            PriceListId = priceList.Id,
            ProductVariantId = variant.Id,
            UnitPrice = 1150m,
            EffectiveFromUtc = DateTime.UtcNow,
        });

        // Two open rows would make "the current price" ambiguous. Raising a
        // price has to close the old row first.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Closing_the_previous_price_lets_a_new_one_open()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"HIST-{suffix}",
            VariantName = "Default",
            IsDefault = true,
        };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        var priceList = await db.PriceLists.FirstAsync(p => p.IsDefault);
        var changedAt = DateTime.UtcNow;

        var original = new PriceListItem
        {
            PriceListId = priceList.Id,
            ProductVariantId = variant.Id,
            UnitPrice = 950m,
            EffectiveFromUtc = changedAt.AddDays(-10),
        };
        db.PriceListItems.Add(original);
        await db.SaveChangesAsync();

        original.EffectiveToUtc = changedAt;
        db.PriceListItems.Add(new PriceListItem
        {
            PriceListId = priceList.Id,
            ProductVariantId = variant.Id,
            UnitPrice = 1150m,
            EffectiveFromUtc = changedAt,
        });
        await db.SaveChangesAsync();

        // Both rows survive: the old price is still answerable, which is the
        // entire reason prices are not a column on the variant.
        var history = await db.PriceListItems
            .Where(i => i.ProductVariantId == variant.Id)
            .OrderBy(i => i.EffectiveFromUtc)
            .ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal(950m, history[0].UnitPrice);
        Assert.NotNull(history[0].EffectiveToUtc);
        Assert.Null(history[1].EffectiveToUtc);
    }

    [Fact]
    public async Task A_product_cannot_have_two_primary_categories()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);

        var first = new Category { Name = $"Cat A {suffix}", Slug = $"cat-a-{suffix}", Path = "/" };
        var second = new Category { Name = $"Cat B {suffix}", Slug = $"cat-b-{suffix}", Path = "/" };

        db.AddRange(product, first, second);
        await db.SaveChangesAsync();

        first.Path = $"/{first.Id}/";
        second.Path = $"/{second.Id}/";
        await db.SaveChangesAsync();

        db.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = first.Id,
            IsPrimary = true,
        });
        await db.SaveChangesAsync();

        db.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = second.Id,
            IsPrimary = true,
        });

        // The primary category drives breadcrumbs and category reporting; two
        // of them would double-count every sale.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_product_may_sit_in_several_categories_with_one_primary()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);

        var primary = new Category { Name = $"Primary {suffix}", Slug = $"primary-{suffix}", Path = "/" };
        var secondary = new Category { Name = $"Secondary {suffix}", Slug = $"secondary-{suffix}", Path = "/" };

        db.AddRange(product, primary, secondary);
        await db.SaveChangesAsync();

        db.ProductCategories.AddRange(
            new ProductCategory { ProductId = product.Id, CategoryId = primary.Id, IsPrimary = true },
            new ProductCategory { ProductId = product.Id, CategoryId = secondary.Id, IsPrimary = false });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.ProductCategories.CountAsync(pc => pc.ProductId == product.Id));
    }

    [Fact]
    public async Task Only_one_gallery_image_per_variant_may_be_primary()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"IMG-{suffix}",
            VariantName = "Ruby Red",
            IsDefault = true,
        };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        // A product-level primary and a variant-level primary coexist happily:
        // they are separate galleries.
        db.ProductImages.AddRange(
            new ProductImage { ProductId = product.Id, Path = "/a.jpg", AltText = "A", IsPrimary = true },
            new ProductImage
            {
                ProductId = product.Id,
                ProductVariantId = variant.Id,
                Path = "/b.jpg",
                AltText = "B",
                IsPrimary = true,
            });

        await db.SaveChangesAsync();

        db.ProductImages.Add(new ProductImage
        {
            ProductId = product.Id,
            ProductVariantId = variant.Id,
            Path = "/c.jpg",
            AltText = "C",
            IsPrimary = true,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sibling_categories_cannot_share_a_slug_but_cousins_can()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();

        var skincare = new Category { Name = $"Skincare {suffix}", Slug = $"skincare-{suffix}", Path = "/" };
        var haircare = new Category { Name = $"Haircare {suffix}", Slug = $"haircare-{suffix}", Path = "/" };
        db.AddRange(skincare, haircare);
        await db.SaveChangesAsync();

        skincare.Path = $"/{skincare.Id}/";
        haircare.Path = $"/{haircare.Id}/";
        await db.SaveChangesAsync();

        // The same slug under two different parents is the point of scoping
        // uniqueness to siblings.
        db.Categories.AddRange(
            new Category { ParentId = skincare.Id, Name = "Cleansers", Slug = "cleansers", Path = $"{skincare.Path}0/", Depth = 1 },
            new Category { ParentId = haircare.Id, Name = "Cleansers", Slug = "cleansers", Path = $"{haircare.Path}0/", Depth = 1 });

        await db.SaveChangesAsync();

        db.Categories.Add(new Category
        {
            ParentId = skincare.Id,
            Name = "Cleansers Again",
            Slug = "cleansers",
            Path = $"{skincare.Path}0/",
            Depth = 1,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_brand_still_holding_products_cannot_be_deleted_from_the_database()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var product = NewProduct(Suffix());
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Raw SQL on purpose: this proves the foreign key is restricted in the
        // database, not merely that the service checks first.
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM catalog.Brands WHERE Id = {0}", _brandId));
    }

    [Fact]
    public async Task Variants_of_a_deleted_product_disappear_from_variant_queries()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Suffix();
        var product = NewProduct(suffix);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductVariants.Add(new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"GONE-{suffix}",
            VariantName = "Default",
            IsDefault = true,
        });
        await db.SaveChangesAsync();

        // The variant is tracked in this same context, which is what the
        // product editor will always have done by the time someone clicks
        // Delete. Before CascadeDeleteTiming was moved to OnSaveChanges, EF
        // cascaded at Remove() - before the soft-delete interceptor could
        // demote the delete - and threw "the association has been severed".
        db.Products.Remove(product);
        await db.SaveChangesAsync();

        var deleted = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == product.Id);
        Assert.True(deleted.IsDeleted);

        // The variant row still exists - the SKU stays reserved so stock
        // history can never be misread - but nothing querying variants sees it.
        Assert.Null(await db.ProductVariants.FirstOrDefaultAsync(v => v.Sku == $"GONE-{suffix}"));
        Assert.NotNull(await db.ProductVariants.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Sku == $"GONE-{suffix}"));
    }
}
