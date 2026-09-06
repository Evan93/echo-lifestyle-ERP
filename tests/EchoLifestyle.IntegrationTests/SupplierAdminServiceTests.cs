using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class SupplierAdminServiceTests
{
    private readonly DatabaseFixture _fixture;

    public SupplierAdminServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static SaveSupplierRequest Request(string name) => new()
    {
        Name = name,
        City = "Dhaka",
        Country = "Bangladesh",
        IsActive = true,
    };

    [Fact]
    public async Task Creating_a_supplier_generates_a_sequential_code()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var created = await service.CreateAsync(Request($"Local distributor {Unique()}"));

        Assert.True(created.Succeeded, created.Error);

        var detail = await service.GetAsync(created.Value);

        Assert.Matches(@"^SUP-\d{3}$", detail!.Code);
        Assert.Equal("BDT", detail.CurrencyCode);
        Assert.False(detail.IsImporter);
    }

    [Fact]
    public async Task Two_suppliers_cannot_share_a_name()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var name = $"Shared name {Unique()}";

        Assert.True((await service.CreateAsync(Request(name))).Succeeded);

        var duplicate = await service.CreateAsync(Request(name));

        Assert.False(duplicate.Succeeded);
        Assert.Equal(nameof(SaveSupplierRequest.Name), duplicate.Field);
    }

    [Fact]
    public async Task A_typed_code_that_is_taken_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var code = $"SUP{Unique()[..5]}".ToUpperInvariant();

        var first = Request($"First {Unique()}");
        first.Code = code;
        Assert.True((await service.CreateAsync(first)).Succeeded);

        var second = Request($"Second {Unique()}");
        second.Code = code;

        var result = await service.CreateAsync(second);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveSupplierRequest.Code), result.Field);
    }

    [Fact]
    public async Task A_local_supplier_cannot_invoice_in_a_foreign_currency()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var request = Request($"Confused supplier {Unique()}");
        request.IsImporter = false;
        request.CurrencyCode = "USD";

        var result = await service.CreateAsync(request);

        // Almost always a mistyped flag rather than a real arrangement, and it
        // would put the landed-cost fields somewhere nobody expects them.
        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveSupplierRequest.CurrencyCode), result.Field);
        Assert.Contains("import source", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_import_source_may_invoice_in_a_foreign_currency()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var request = Request($"Korean supplier {Unique()}");
        request.IsImporter = true;
        request.CurrencyCode = "usd";
        request.Country = "South Korea";

        var created = await service.CreateAsync(request);

        Assert.True(created.Succeeded, created.Error);

        var detail = await service.GetAsync(created.Value);

        Assert.True(detail!.IsImporter);
        Assert.Equal("USD", detail.CurrencyCode);
    }

    [Fact]
    public async Task Payment_terms_beyond_a_year_are_refused()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var request = Request($"Patient supplier {Unique()}");
        request.PaymentTermDays = 400;

        var result = await service.CreateAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveSupplierRequest.PaymentTermDays), result.Field);
    }

    [Fact]
    public async Task A_supplier_with_no_deliveries_can_be_deleted_and_frees_its_name()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var name = $"Transient supplier {Unique()}";

        var created = await service.CreateAsync(Request(name));
        Assert.True(created.Succeeded, created.Error);

        var deleted = await service.DeleteAsync(created.Value);
        Assert.True(deleted.Succeeded, deleted.Error);
        Assert.Null(await service.GetAsync(created.Value));

        // Both unique indexes are filtered on IsDeleted, so the name comes back
        // into circulation.
        Assert.True((await service.CreateAsync(Request(name))).Succeeded);
    }

    [Fact]
    public async Task A_supplier_who_has_delivered_stock_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();

        var created = await service.CreateAsync(Request($"Delivering supplier {suffix}"));
        Assert.True(created.Succeeded, created.Error);

        var variantId = await SeedVariantAsync(db, suffix);

        db.StockBatches.Add(new StockBatch
        {
            ProductVariantId = variantId,
            BatchNumber = $"SUP-{suffix}",
            ReceivedDate = new DateOnly(2026, 9, 1),
            LandedUnitCost = 250m,
            SupplierId = created.Value,
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(created.Value);

        // A batch has to keep pointing at somebody you can call about a recall.
        Assert.False(result.Succeeded);
        Assert.Contains("deactivate", result.Error, StringComparison.OrdinalIgnoreCase);

        var detail = await service.GetAsync(created.Value);
        Assert.Equal(1, detail!.BatchCount);
    }

    [Fact]
    public async Task An_inactive_supplier_still_appears_when_editing_a_document_that_uses_it()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var created = await service.CreateAsync(Request($"Retired supplier {Unique()}"));
        var detail = await service.GetAsync(created.Value);

        var deactivate = Request(detail!.Name);
        deactivate.Code = detail.Code;
        deactivate.IsActive = false;

        Assert.True((await service.UpdateAsync(created.Value, deactivate)).Succeeded);

        var withoutIt = await service.GetOptionsAsync();
        var withIt = await service.GetOptionsAsync(created.Value);

        Assert.DoesNotContain(withoutIt, o => o.Id == created.Value);
        Assert.Contains(withIt, o => o.Id == created.Value);
    }

    [Fact]
    public async Task The_status_filter_separates_active_from_inactive()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var suffix = Unique();

        var active = Request($"Filter active {suffix}");
        var inactive = Request($"Filter inactive {suffix}");
        inactive.IsActive = false;

        Assert.True((await service.CreateAsync(active)).Succeeded);
        Assert.True((await service.CreateAsync(inactive)).Succeeded);

        var activeRows = await service.ListAsync(suffix, 0, 50, null, false, StatusFilter.Active);
        var inactiveRows = await service.ListAsync(suffix, 0, 50, null, false, StatusFilter.Inactive);

        Assert.Single(activeRows.Rows);
        Assert.Single(inactiveRows.Rows);
        Assert.Equal($"Filter active {suffix}", activeRows.Rows[0].Name);
    }

    private static async Task<long> SeedVariantAsync(EchoDbContext db, string suffix)
    {
        var brand = new Brand { Name = $"Supplier brand {suffix}", Slug = $"supplier-brand-{suffix}" };
        var unit = await db.UnitsOfMeasure.FirstAsync(u => u.Code == "PC");

        db.Brands.Add(brand);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Code = $"SP-{suffix}",
            Name = $"Supplier product {suffix}",
            Slug = $"supplier-product-{suffix}",
            BrandId = brand.Id,
            UnitOfMeasureId = unit.Id,
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"SPSKU-{suffix}",
            VariantName = "Standard",
            IsDefault = true,
        };

        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();

        return variant.Id;
    }
}
