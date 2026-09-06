using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Purchasing;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Receiving stock - the first place in the system where stock actually moves.
///
/// These assert the whole chain, not just the document: a posted receipt has to
/// leave a batch, a ledger entry and a balance that agree with each other, or
/// rule 1 is a comment rather than a guarantee.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class GoodsReceiptServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public GoodsReceiptServiceTests(DatabaseFixture fixture)
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

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        // The seeded branch and warehouse, which DatabaseFixture now guarantees.
        _branchId = await db.Branches.Where(b => b.IsActive).Select(b => b.Id).FirstAsync();
        _warehouseId = await db.Warehouses.Where(w => w.IsActive).Select(w => w.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private async Task<long> SupplierAsync(IServiceProvider services, string suffix, bool importer = false)
    {
        var suppliers = services.GetRequiredService<SupplierAdminService>();

        var created = await suppliers.CreateAsync(new SaveSupplierRequest
        {
            Name = $"Receipt supplier {suffix}",
            IsImporter = importer,
            CurrencyCode = importer ? "USD" : "BDT",
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        return created.Value;
    }

    private async Task<long> VariantAsync(
        IServiceProvider services,
        string suffix,
        bool batchTracked = false,
        bool expiryTracked = false,
        int? shelfLifeDays = null,
        decimal? weightGrams = null)
    {
        var brands = services.GetRequiredService<BrandAdminService>();
        var categories = services.GetRequiredService<CategoryAdminService>();
        var products = services.GetRequiredService<ProductAdminService>();
        var db = services.GetRequiredService<EchoDbContext>();

        var brand = await brands.CreateAsync(new SaveBrandRequest
        {
            Name = $"Receipt brand {suffix}",
            IsActive = true,
        });

        var category = await categories.CreateAsync(new SaveCategoryRequest
        {
            Name = $"Receipt category {suffix}",
            IsActive = true,
        });

        var created = await products.QuickCreateAsync(new QuickCreateProductRequest
        {
            Name = $"Receipt product {suffix}",
            BrandId = brand.Value,
            CategoryId = category.Value,
            Price = 900m,
            IsBatchTracked = batchTracked,
            IsExpiryTracked = expiryTracked,
            IsActive = true,
        });

        Assert.True(created.Succeeded, created.Error);

        if (shelfLifeDays is not null || weightGrams is not null)
        {
            var product = await db.Products
                .Include(p => p.Variants)
                .FirstAsync(p => p.Id == created.Value);

            product.ShelfLifeDays = shelfLifeDays;
            product.Variants.First().WeightGrams = weightGrams;

            await db.SaveChangesAsync();
        }

        return (await products.GetAsync(created.Value))!.Variants.First().Id;
    }

    private QuickPurchaseRequest Request(long supplierId, params ReceiptLineInput[] lines) => new()
    {
        SupplierId = supplierId,
        WarehouseId = _warehouseId,
        BranchId = _branchId,
        ReceiptDate = new DateOnly(2026, 9, 6),
        Lines = lines,
    };

    // -----------------------------------------------------------------------
    // The happy path, end to end
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Posting_a_receipt_creates_a_batch_a_ledger_entry_and_a_balance_that_agree()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        var posted = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 24m, UnitCost = 320m }));

        Assert.True(posted.Succeeded, posted.Error);

        var receipt = await db.GoodsReceipts
            .Include(r => r.Lines)
            .FirstAsync(r => r.Id == posted.Value);

        Assert.Equal(GoodsReceiptStatus.Posted, receipt.Status);
        Assert.StartsWith("GRN-2609-", receipt.Number);
        Assert.Equal(7680m, receipt.SubTotal);
        Assert.Equal(0m, receipt.ChargeTotal);
        Assert.Equal(7680m, receipt.GrandTotal);

        var batch = await db.StockBatches.SingleAsync(b => b.GoodsReceiptId == receipt.Id);
        Assert.True(batch.IsAutoGenerated);
        Assert.Equal(320m, batch.LandedUnitCost);
        Assert.Equal(supplierId, batch.SupplierId);

        // Scoped to this test's own variant, not to the document id. Document
        // ids are only unique within a document type, and other classes write
        // synthetic ledger rows - so "the entry for receipt 1" is not a question
        // with one answer.
        var entry = await db.StockLedger.SingleAsync(e => e.ProductVariantId == variantId);
        Assert.Equal(StockMovementType.Receipt, entry.MovementType);
        Assert.Equal(receipt.Id, entry.DocumentId);
        Assert.Equal(StockDocumentType.GoodsReceipt, entry.DocumentType);
        Assert.Equal(receipt.Number, entry.DocumentNumber);
        Assert.Equal(24m, entry.QuantityChange);
        Assert.Equal(320m, entry.UnitCost);
        Assert.Equal(7680m, entry.ValueChange);
        Assert.Equal(batch.Id, entry.StockBatchId);
        Assert.Equal(new DateOnly(2026, 9, 6), entry.BusinessDate);

        var balance = await db.StockBalances.SingleAsync(b => b.StockBatchId == batch.Id);
        Assert.Equal(24m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.QuantityReserved);

        // The projection and the ledger must agree, because one is derived from
        // the other.
        var fromLedger = await db.StockLedger
            .Where(e => e.StockBatchId == batch.Id)
            .SumAsync(e => e.QuantityChange);

        Assert.Equal(balance.QuantityOnHand, fromLedger);
    }

    [Fact]
    public async Task An_untracked_product_gets_a_generated_batch_nobody_has_to_type()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix, batchTracked: false);

        var posted = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 5m, UnitCost = 100m }));

        Assert.True(posted.Succeeded, posted.Error);

        var batch = await db.StockBatches.SingleAsync(b => b.GoodsReceiptId == posted.Value);
        var receipt = await db.GoodsReceipts.FirstAsync(r => r.Id == posted.Value);

        // Light entry, as agreed: traceability without the typing. The number is
        // derived from the receipt so a recall can still find it.
        Assert.True(batch.IsAutoGenerated);
        Assert.Equal($"{receipt.Number}-01", batch.BatchNumber);
    }

    [Fact]
    public async Task Receipt_numbers_run_in_sequence_within_a_month()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        var first = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 1m, UnitCost = 10m }));

        var second = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 1m, UnitCost = 10m }));

        var firstNumber = (await db.GoodsReceipts.FirstAsync(r => r.Id == first.Value)).Number;
        var secondNumber = (await db.GoodsReceipts.FirstAsync(r => r.Id == second.Value)).Number;

        Assert.NotEqual(firstNumber, secondNumber);

        var firstSeq = int.Parse(firstNumber[^4..]);
        var secondSeq = int.Parse(secondNumber[^4..]);

        Assert.Equal(firstSeq + 1, secondSeq);
    }

    // -----------------------------------------------------------------------
    // Landed cost
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_import_converts_at_the_documents_rate_and_spreads_its_charges()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix, importer: true);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        var request = Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 100m, UnitCost = 3m });

        request.ExchangeRate = 118m;
        request.Charges =
        [
            new ReceiptChargeInput { ChargeType = PurchaseChargeType.Freight, Amount = 9000m },
            new ReceiptChargeInput { ChargeType = PurchaseChargeType.CustomsDuty, Amount = 14000m },
        ];

        var posted = await receipts.PostQuickPurchaseAsync(request);
        Assert.True(posted.Succeeded, posted.Error);

        var receipt = await db.GoodsReceipts
            .Include(r => r.Lines)
            .Include(r => r.Charges)
            .FirstAsync(r => r.Id == posted.Value);

        Assert.Equal("USD", receipt.CurrencyCode);
        Assert.Equal(118m, receipt.ExchangeRate);
        Assert.Equal(35400m, receipt.SubTotal);
        Assert.Equal(23000m, receipt.ChargeTotal);
        Assert.Equal(58400m, receipt.GrandTotal);
        Assert.Equal(2, receipt.Charges.Count);

        var line = receipt.Lines.Single();

        // Everything on one line, so it carries all of the charges.
        Assert.Equal(3m, line.UnitCost);
        Assert.Equal(35400m, line.LineTotalBase);
        Assert.Equal(23000m, line.ApportionedCharge);
        Assert.Equal(584m, line.LandedUnitCost);

        // The ledger records the landed figure, not the invoice price. That is
        // the number margin is measured against.
        var entry = await db.StockLedger.SingleAsync(e => e.ProductVariantId == variantId);
        Assert.Equal(584m, entry.UnitCost);
        Assert.Equal(58400m, entry.ValueChange);
    }

    [Fact]
    public async Task Charges_are_ignored_on_a_local_purchase()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix, importer: false);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        var request = Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 10m, UnitCost = 50m });

        request.ExchangeRate = 118m;
        request.Charges =
        [
            new ReceiptChargeInput { ChargeType = PurchaseChargeType.Freight, Amount = 5000m },
        ];

        var posted = await receipts.PostQuickPurchaseAsync(request);
        Assert.True(posted.Succeeded, posted.Error);

        var receipt = await db.GoodsReceipts
            .Include(r => r.Charges)
            .FirstAsync(r => r.Id == posted.Value);

        // A rate and a freight bill posted against a local supplier are noise at
        // best and a typo at worst. Both are dropped rather than trusted.
        Assert.Equal("BDT", receipt.CurrencyCode);
        Assert.Equal(1m, receipt.ExchangeRate);
        Assert.Equal(500m, receipt.GrandTotal);
        Assert.Empty(receipt.Charges);
    }

    [Fact]
    public async Task Charges_spread_across_lines_and_still_reconcile_to_the_total()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix, importer: true);

        var first = await VariantAsync(scope.ServiceProvider, suffix + "a");
        var second = await VariantAsync(scope.ServiceProvider, suffix + "b");
        var third = await VariantAsync(scope.ServiceProvider, suffix + "c");

        var request = Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = first, Quantity = 1m, UnitCost = 100m },
            new ReceiptLineInput { ProductVariantId = second, Quantity = 1m, UnitCost = 100m },
            new ReceiptLineInput { ProductVariantId = third, Quantity = 1m, UnitCost = 100m });

        request.ExchangeRate = 1m;
        request.Charges =
        [
            new ReceiptChargeInput { ChargeType = PurchaseChargeType.Clearing, Amount = 1000m },
        ];

        var posted = await receipts.PostQuickPurchaseAsync(request);
        Assert.True(posted.Succeeded, posted.Error);

        var receipt = await db.GoodsReceipts.Include(r => r.Lines).FirstAsync(r => r.Id == posted.Value);

        // A third of 1,000 does not divide evenly. The parts still add up to the
        // whole, or the receipt could not be reconciled against the invoice.
        Assert.Equal(1000m, receipt.Lines.Sum(l => l.ApportionedCharge));
        Assert.Equal(1300m, receipt.GrandTotal);
    }

    // -----------------------------------------------------------------------
    // Batch and expiry rules
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_batch_tracked_product_demands_the_suppliers_batch_number()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix, batchTracked: true);

        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 5m, UnitCost = 100m }));

        Assert.False(result.Succeeded);
        Assert.Contains("batch number", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_batch_number_cannot_be_received_twice_for_one_product()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix, batchTracked: true);

        var line = new ReceiptLineInput
        {
            ProductVariantId = variantId,
            Quantity = 5m,
            UnitCost = 100m,
            BatchNumber = "LOT-24A",
        };

        Assert.True((await receipts.PostQuickPurchaseAsync(Request(supplierId, line))).Succeeded);

        var second = await receipts.PostQuickPurchaseAsync(Request(supplierId, line));

        // A batch carries one cost and one expiry. Receiving the number again at
        // a different cost would make one of those figures a lie.
        Assert.False(second.Succeeded);
        Assert.Contains("already been received", second.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suffix", second.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_batch_number_twice_on_one_receipt_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix, batchTracked: true);

        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 5m, UnitCost = 100m, BatchNumber = "LOT-9" },
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 3m, UnitCost = 110m, BatchNumber = "LOT-9" }));

        // Caught here rather than at the unique index, so the message says which
        // batch and what to do.
        Assert.False(result.Succeeded);
        Assert.Contains("twice", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expiry_is_worked_out_from_a_manufacture_date_and_the_shelf_life()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);

        var variantId = await VariantAsync(
            scope.ServiceProvider, suffix, batchTracked: true, expiryTracked: true, shelfLifeDays: 730);

        var posted = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput
            {
                ProductVariantId = variantId,
                Quantity = 6m,
                UnitCost = 250m,
                BatchNumber = $"MFG-{suffix}",
                ManufactureDate = new DateOnly(2026, 1, 15),
            }));

        Assert.True(posted.Succeeded, posted.Error);

        var batch = await db.StockBatches.SingleAsync(b => b.GoodsReceiptId == posted.Value);

        // Asking for the expiry date as well would be asking twice for the same
        // fact. 730 days from 15 January 2026 is 15 January 2028 - neither year
        // crossed adds a leap day before January.
        Assert.Equal(new DateOnly(2028, 1, 15), batch.ExpiryDate);
    }

    [Fact]
    public async Task An_expiry_tracked_product_with_no_date_and_no_shelf_life_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);

        var variantId = await VariantAsync(
            scope.ServiceProvider, suffix, batchTracked: true, expiryTracked: true);

        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput
            {
                ProductVariantId = variantId,
                Quantity = 6m,
                UnitCost = 250m,
                BatchNumber = $"NOEXP-{suffix}",
            }));

        Assert.False(result.Succeeded);
        Assert.Contains("expiry", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stock_that_has_already_expired_cannot_be_received_as_sellable()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);

        var variantId = await VariantAsync(
            scope.ServiceProvider, suffix, batchTracked: true, expiryTracked: true);

        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput
            {
                ProductVariantId = variantId,
                Quantity = 6m,
                UnitCost = 250m,
                BatchNumber = $"OLD-{suffix}",
                ExpiryDate = new DateOnly(2026, 8, 1),
            }));

        // Almost always a mistyped year. Accepting it would put expired stock on
        // the shop as available.
        Assert.False(result.Succeeded);
        Assert.Contains("expires", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Refusals leave nothing behind
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_refused_receipt_writes_nothing_at_all()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var good = await VariantAsync(scope.ServiceProvider, suffix + "ok");
        var tracked = await VariantAsync(scope.ServiceProvider, suffix + "bad", batchTracked: true);

        var before = await db.GoodsReceipts.CountAsync();

        // The second line is missing its batch number, so the whole thing is
        // refused - including the perfectly good first line.
        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = good, Quantity = 5m, UnitCost = 100m },
            new ReceiptLineInput { ProductVariantId = tracked, Quantity = 5m, UnitCost = 100m }));

        Assert.False(result.Succeeded);

        // No half-finished document for somebody to find later and wonder about.
        Assert.Equal(before, await db.GoodsReceipts.CountAsync());
        Assert.Empty(await db.StockLedger.Where(e => e.ProductVariantId == good).ToListAsync());
        Assert.Empty(await db.StockBalances.Where(b => b.ProductVariantId == good).ToListAsync());
    }

    [Fact]
    public async Task An_empty_receipt_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var supplierId = await SupplierAsync(scope.ServiceProvider, Unique());

        var result = await receipts.PostQuickPurchaseAsync(Request(supplierId));

        Assert.False(result.Succeeded);
        Assert.Contains("at least one line", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_inactive_supplier_cannot_deliver()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var suppliers = scope.ServiceProvider.GetRequiredService<SupplierAdminService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        var detail = await suppliers.GetAsync(supplierId);

        Assert.True((await suppliers.UpdateAsync(supplierId, new SaveSupplierRequest
        {
            Code = detail!.Code,
            Name = detail.Name,
            IsActive = false,
        })).Succeeded);

        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 1m, UnitCost = 10m }));

        Assert.False(result.Succeeded);
        Assert.Contains("inactive", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_discount_larger_than_the_line_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        var result = await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput
            {
                ProductVariantId = variantId,
                Quantity = 2m,
                UnitCost = 100m,
                DiscountAmount = 500m,
            }));

        // A negative line total would apportion charges backwards and produce a
        // negative landed cost.
        Assert.False(result.Succeeded);
        Assert.Contains("discount", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_receipts_of_the_same_product_accumulate_into_separate_batches()
    {
        await using var scope = _fixture.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<GoodsReceiptService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var suffix = Unique();
        var supplierId = await SupplierAsync(scope.ServiceProvider, suffix);
        var variantId = await VariantAsync(scope.ServiceProvider, suffix);

        await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 10m, UnitCost = 300m }));

        await receipts.PostQuickPurchaseAsync(Request(
            supplierId,
            new ReceiptLineInput { ProductVariantId = variantId, Quantity = 10m, UnitCost = 350m }));

        var balances = await db.StockBalances
            .Where(b => b.ProductVariantId == variantId)
            .ToListAsync();

        // Two deliveries at two prices are two batches, each with its own true
        // cost - which is the whole reason for costing by batch rather than
        // blending into an average.
        Assert.Equal(2, balances.Count);
        Assert.Equal(20m, balances.Sum(b => b.QuantityOnHand));

        var batches = await db.StockBatches
            .Where(b => b.ProductVariantId == variantId)
            .OrderBy(b => b.Id)
            .ToListAsync();

        Assert.Equal(300m, batches[0].LandedUnitCost);
        Assert.Equal(350m, batches[1].LandedUnitCost);
    }
}
