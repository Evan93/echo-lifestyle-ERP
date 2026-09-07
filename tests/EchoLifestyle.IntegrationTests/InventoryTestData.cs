using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Building the stock a test needs: a supplier, a product, a received batch.
///
/// Shared because adjustments, counts and the stock queries all need the same
/// three things and three near-identical copies would drift. Everything goes
/// through the real services, so a test never sets up state the application
/// could not have produced.
/// </summary>
public static class InventoryTestData
{
    public static string Unique() => Guid.NewGuid().ToString("N")[..8];

    public static async Task<long> SupplierAsync(IServiceProvider services, string suffix)
    {
        var suppliers = services.GetRequiredService<SupplierAdminService>();

        var created = await suppliers.CreateAsync(new SaveSupplierRequest
        {
            Name = $"Inventory supplier {suffix}",
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);
        return created.Value;
    }

    public static async Task<VariantHandle> VariantAsync(
        IServiceProvider services,
        string suffix,
        bool expiryTracked = false,
        long? brandId = null)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();
        var products = services.GetRequiredService<ProductAdminService>();

        var brand = brandId;

        if (brand is null)
        {
            var created = await brands.CreateAsync(new SaveBrandRequest
            {
                Name = $"Inventory brand {suffix}",
                IsActive = true,
            });

            Assert.True(created.Succeeded, created.Error);
            brand = created.Value;
        }

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Inventory category {suffix}",
            IsActive = true,
        });

        Assert.True(category.Succeeded, category.Error);

        var product = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Inventory product {suffix}",
            BrandId = brand.Value,
            CategoryId = category.Value,
            Price = 900m,
            IsBatchTracked = expiryTracked,
            IsExpiryTracked = expiryTracked,
            IsActive = true,
        });

        Assert.True(product.Succeeded, product.Error);

        var variant = (await products.GetAsync(product.Value))!.Variants.First();

        return new VariantHandle
        {
            VariantId = variant.Id,
            Sku = variant.Sku,
            BrandId = brand.Value,
            CategoryId = category.Value,
            ProductId = product.Value,
        };
    }

    /// <summary>Receives stock and returns the batch it created.</summary>
    public static async Task<long> ReceiveAsync(
        IServiceProvider services,
        long supplierId,
        long warehouseId,
        long branchId,
        long variantId,
        decimal quantity,
        decimal unitCost,
        string? batchNumber = null,
        DateOnly? expiry = null)
    {
        var receipts = services.GetRequiredService<GoodsReceiptService>();

        var posted = await receipts.PostQuickPurchaseAsync(new QuickPurchaseRequest
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            BranchId = branchId,
            ReceiptDate = new DateOnly(2026, 9, 6),
            Lines =
            [
                new ReceiptLineInput
                {
                    ProductVariantId = variantId,
                    Quantity = quantity,
                    UnitCost = unitCost,
                    BatchNumber = batchNumber,
                    ExpiryDate = expiry,
                },
            ],
        });

        Assert.True(posted.Succeeded, posted.Error);

        var db = services.GetRequiredService<EchoDbContext>();

        // Looked up through the receipt rather than by guessing at the
        // generated batch number, which is an implementation detail this helper
        // has no business encoding.
        return await db.StockBatches
            .Where(b => b.ProductVariantId == variantId && b.GoodsReceiptId == posted.Value)
            .Select(b => b.Id)
            .FirstAsync();
    }
}

public class VariantHandle
{
    public long VariantId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public long BrandId { get; init; }

    public long CategoryId { get; init; }

    public long ProductId { get; init; }
}
