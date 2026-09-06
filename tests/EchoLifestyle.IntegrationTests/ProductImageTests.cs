using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class ProductImageTests
{
    private readonly DatabaseFixture _fixture;

    public ProductImageTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// The store only reads the first few bytes to decide whether a file is
    /// what it claims to be, so a real photograph is not needed - and using one
    /// would make the test about decoding rather than about the check.
    /// </summary>
    private static MemoryStream WithHeader(byte[] header, int padding = 64)
    {
        var bytes = new byte[header.Length + padding];
        header.CopyTo(bytes, 0);

        return new MemoryStream(bytes);
    }

    private static MemoryStream Jpeg() => WithHeader([0xFF, 0xD8, 0xFF, 0xE0]);

    private static MemoryStream Png() =>
        WithHeader([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    private static async Task<long> CreateProductAsync(IServiceProvider services, string suffix)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();
        var products = services.GetRequiredService<ProductAdminService>();

        var brand = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Image brand {suffix}",
            IsActive = true,
        });

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Image category {suffix}",
            IsActive = true,
        });

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Image product {suffix}",
            BrandId = brand.Value,
            CategoryId = category.Value,
            Price = 500m,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        return created.Value;
    }

    [Fact]
    public async Task The_first_image_in_a_gallery_becomes_its_main_one()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        var added = await products.AddImageAsync(id, new UploadImageRequest
        {
            Content = Jpeg(),
            Kind = FileKind.Jpeg,
        });

        Assert.True(added.Succeeded, added.Error);

        var image = Assert.Single(await products.GetImagesAsync(id));

        // Nobody uploading a single photograph should also have to nominate it.
        Assert.True(image.IsPrimary);
        Assert.StartsWith("/uploads/products/", image.Path);
        Assert.EndsWith(".jpg", image.Path);
    }

    [Fact]
    public async Task The_stored_file_is_named_by_the_store_and_actually_written()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.AddImageAsync(id, new UploadImageRequest
        {
            Content = Jpeg(),
            Kind = FileKind.Jpeg,
        });

        var image = Assert.Single(await products.GetImagesAsync(id));

        var absolute = Path.Combine(
            _fixture.FileRoot,
            image.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(absolute), $"Expected a file at {absolute}");

        // A GUID name, not the caller's: nothing a browser sent decides where a
        // file lands or what it is called.
        Assert.Matches(@"^[0-9a-f]{32}\.jpg$", Path.GetFileName(absolute));
    }

    [Fact]
    public async Task A_file_whose_bytes_do_not_match_its_declared_type_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        // PNG bytes, declared as a JPEG. The oldest upload trick there is: the
        // extension and the content-type header both come from the browser and
        // neither is evidence of anything.
        var result = await products.AddImageAsync(id, new UploadImageRequest
        {
            Content = Png(),
            Kind = FileKind.Jpeg,
        });

        Assert.False(result.Succeeded);
        Assert.Contains("not the image type it claims", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await products.GetImagesAsync(id));
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        var result = await products.AddImageAsync(id, new UploadImageRequest
        {
            Content = new MemoryStream(),
            Kind = FileKind.Jpeg,
        });

        Assert.False(result.Succeeded);
        Assert.Empty(await products.GetImagesAsync(id));
    }

    [Fact]
    public async Task A_variant_gallery_is_separate_from_the_product_gallery()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.SaveOptionsAsync(id, new SaveOptionsRequest
        {
            Options = [new OptionInput { Name = "Shade", Values = "Ruby Red #C21807, Coral" }],
        });

        var variant = (await products.GetAsync(id))!.Variants.First();

        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var shadeShot = await products.AddImageAsync(id, new UploadImageRequest
        {
            Content = Jpeg(),
            Kind = FileKind.Jpeg,
            ProductVariantId = variant.Id,
        });

        Assert.True(shadeShot.Succeeded, shadeShot.Error);

        var images = await products.GetImagesAsync(id);

        // Both are primary, because they are primary in different galleries -
        // which is exactly what the filtered unique index allows.
        Assert.Equal(2, images.Count);
        Assert.All(images, i => Assert.True(i.IsPrimary));
        Assert.Single(images, i => i.ProductVariantId == variant.Id);
        Assert.Single(images, i => i.ProductVariantId is null);
    }

    [Fact]
    public async Task An_image_cannot_be_attached_to_another_products_variant()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var mine = await CreateProductAsync(scope.ServiceProvider, Unique());
        var theirs = await CreateProductAsync(scope.ServiceProvider, Unique());

        var foreignVariant = (await products.GetAsync(theirs))!.Variants.Single();

        var result = await products.AddImageAsync(mine, new UploadImageRequest
        {
            Content = Jpeg(),
            Kind = FileKind.Jpeg,
            ProductVariantId = foreignVariant.Id,
        });

        // The variant id arrives from the browser; the check that it belongs to
        // the product in the route is the only thing standing between a stray
        // id and someone else's gallery.
        Assert.False(result.Succeeded);
        Assert.Contains("does not belong", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Alt_text_defaults_to_the_product_name_and_can_be_corrected()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var suffix = Unique();
        var id = await CreateProductAsync(scope.ServiceProvider, suffix);

        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var image = Assert.Single(await products.GetImagesAsync(id));

        // Blocking the upload over alt text would be obstructive; leaving it
        // empty would put an unreadable image on the storefront.
        Assert.Equal($"Image product {suffix}", image.AltText);

        var saved = await products.SaveImagesAsync(id, new SaveImagesRequest
        {
            Images = [new ImageInput { Id = image.Id, AltText = "Bottle, front", DisplayOrder = 0, IsPrimary = true }],
        });

        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal("Bottle, front", (await products.GetImagesAsync(id)).Single().AltText);
    }

    [Fact]
    public async Task Blank_alt_text_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var image = Assert.Single(await products.GetImagesAsync(id));

        var result = await products.SaveImagesAsync(id, new SaveImagesRequest
        {
            Images = [new ImageInput { Id = image.Id, AltText = "   ", DisplayOrder = 0, IsPrimary = true }],
        });

        Assert.False(result.Succeeded);
        Assert.Contains("alt text", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_main_images_in_one_gallery_are_refused_before_the_database_sees_it()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });
        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var images = await products.GetImagesAsync(id);

        var result = await products.SaveImagesAsync(id, new SaveImagesRequest
        {
            Images = images
                .Select((i, index) => new ImageInput
                {
                    Id = i.Id,
                    AltText = "Both main",
                    DisplayOrder = index,
                    IsPrimary = true,
                })
                .ToList(),
        });

        Assert.False(result.Succeeded);
        Assert.Contains("only one image", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Moving_the_main_image_leaves_exactly_one()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });
        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var images = (await products.GetImagesAsync(id)).OrderBy(i => i.DisplayOrder).ToList();

        var result = await products.SaveImagesAsync(id, new SaveImagesRequest
        {
            Images = images
                .Select((i, index) => new ImageInput
                {
                    Id = i.Id,
                    AltText = "Shot " + index,
                    DisplayOrder = index,
                    IsPrimary = index == 1,
                })
                .ToList(),
        });

        Assert.True(result.Succeeded, result.Error);

        // The filtered unique index would have rejected an overlap, so this
        // passing is what proves the flag is cleared before the new one is set.
        var after = await products.GetImagesAsync(id);
        Assert.Single(after, i => i.IsPrimary);
        Assert.Equal(images[1].Id, after.Single(i => i.IsPrimary).Id);
    }

    [Fact]
    public async Task Deleting_the_main_image_promotes_the_next_one_and_removes_the_file()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });
        await products.AddImageAsync(id, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var images = (await products.GetImagesAsync(id)).OrderBy(i => i.DisplayOrder).ToList();
        var primary = images.Single(i => i.IsPrimary);

        var absolute = Path.Combine(
            _fixture.FileRoot,
            primary.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(absolute));

        var deleted = await products.DeleteImageAsync(id, primary.Id);
        Assert.True(deleted.Succeeded, deleted.Error);

        var remaining = Assert.Single(await products.GetImagesAsync(id));

        // A gallery with no main image has no thumbnail to show in a listing.
        Assert.True(remaining.IsPrimary);
        Assert.False(File.Exists(absolute));
    }

    [Fact]
    public async Task An_image_belonging_to_another_product_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();

        var mine = await CreateProductAsync(scope.ServiceProvider, Unique());
        var theirs = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.AddImageAsync(theirs, new UploadImageRequest { Content = Jpeg(), Kind = FileKind.Jpeg });

        var theirImage = Assert.Single(await products.GetImagesAsync(theirs));

        // The id comes from the browser, so the delete is scoped to the product
        // in the route rather than trusting the id on its own.
        var result = await products.DeleteImageAsync(mine, theirImage.Id);

        Assert.False(result.Succeeded);
        Assert.Single(await products.GetImagesAsync(theirs));
    }

    [Fact]
    public async Task A_variant_with_images_is_retired_rather_than_removed()
    {
        await using var scope = _fixture.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<ProductAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var id = await CreateProductAsync(scope.ServiceProvider, Unique());

        await products.SaveOptionsAsync(id, new SaveOptionsRequest
        {
            Options = [new OptionInput { Name = "Shade", Values = "Ruby Red, Coral" }],
        });

        var coral = (await products.GetAsync(id))!.Variants.Single(v => v.VariantName == "Coral");

        await products.AddImageAsync(id, new UploadImageRequest
        {
            Content = Jpeg(),
            Kind = FileKind.Jpeg,
            ProductVariantId = coral.Id,
        });

        var rebuilt = await products.SaveOptionsAsync(id, new SaveOptionsRequest
        {
            Options = [new OptionInput { Name = "Shade", Values = "Ruby Red" }],
        });

        Assert.True(rebuilt.Succeeded, rebuilt.Error);

        // Deleting it would leave a photograph pointing at nothing, and the
        // restricted foreign key would have refused the delete anyway.
        Assert.Equal(1, rebuilt.Value!.Retired);
        Assert.Equal(0, rebuilt.Value.Removed);

        var survivor = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == coral.Id);
        Assert.NotNull(survivor);
        Assert.False(survivor!.IsActive);
    }
}
