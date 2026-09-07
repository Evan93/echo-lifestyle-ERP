using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Application.Purchasing.LandedCost;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Purchasing.Receiving;

/// <summary>
/// Receiving stock.
///
/// The whole of Quick Purchase is <see cref="PostQuickPurchaseAsync"/>: it
/// validates, creates the document, creates batches, appends ledger entries and
/// moves balances, all inside one transaction. There is no draft state to get
/// stuck in and no second step to forget.
///
/// The formal purchase-order route will produce the same document through the
/// same posting code. Stock must have exactly one way into this system, or the
/// two paths will drift and only one of them will be right.
/// </summary>
public partial class GoodsReceiptService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public GoodsReceiptService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>
    /// Receives a delivery and posts it in one go.
    ///
    /// Everything that can be refused is refused before anything is written, so
    /// a rejected receipt leaves no half-finished document behind for somebody
    /// to find later and wonder about.
    /// </summary>
    public async Task<OperationResult<long>> PostQuickPurchaseAsync(
        QuickPurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, cancellationToken);

        if (!prepared.Succeeded)
        {
            return OperationResult<long>.Failure(prepared.Error!, prepared.Field);
        }

        var plan = prepared.Value!;

        return await _db.ExecuteInTransactionAsync(
            async token => await WriteAsync(plan, token),
            cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Validation and costing - no writes
    // -----------------------------------------------------------------------

    private async Task<OperationResult<ReceiptPlan>> PrepareAsync(
        QuickPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
        {
            return Fail("Add at least one line - a receipt with nothing in it moves no stock.");
        }

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);

        if (supplier is null)
        {
            return Fail("Choose a supplier.", nameof(QuickPurchaseRequest.SupplierId));
        }

        if (!supplier.IsActive)
        {
            return Fail(
                $"{supplier.Name} is inactive. Reactivate them before receiving stock from them.",
                nameof(QuickPurchaseRequest.SupplierId));
        }

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId, cancellationToken);

        if (warehouse is null || !warehouse.IsActive)
        {
            return Fail(
                "Choose an active warehouse to receive into.", nameof(QuickPurchaseRequest.WarehouseId));
        }

        if (!await _db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, cancellationToken))
        {
            return Fail("Choose an active branch.", nameof(QuickPurchaseRequest.BranchId));
        }

        // A local supplier is BDT at 1, whatever the form posted. Trusting a
        // rate here would let a typo silently multiply every cost on the
        // document.
        var rate = supplier.IsImporter ? request.ExchangeRate ?? 1m : 1m;

        if (rate <= 0m)
        {
            return Fail(
                "The exchange rate has to be greater than zero.",
                nameof(QuickPurchaseRequest.ExchangeRate));
        }

        var variantIds = request.Lines.Select(l => l.ProductVariantId).Distinct().ToList();

        var variants = await _db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.Sku,
                v.WeightGrams,
                v.IsActive,
                ProductName = v.Product!.Name,
                v.Product.IsBatchTracked,
                v.Product.IsExpiryTracked,
                v.Product.ShelfLifeDays,
            })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var receiptDate = request.ReceiptDate ?? _clock.ToBusinessDate(_clock.UtcNow);

        var plan = new ReceiptPlan
        {
            Supplier = supplier,
            WarehouseId = warehouse.Id,
            BranchId = request.BranchId,
            ReceiptDate = receiptDate,
            CurrencyCode = supplier.IsImporter ? supplier.CurrencyCode : "BDT",
            ExchangeRate = rate,
            SupplierInvoiceNumber = Trim(request.SupplierInvoiceNumber),
            SupplierInvoiceDate = request.SupplierInvoiceDate,
            Notes = Trim(request.Notes),
        };

        var seenBatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in request.Lines)
        {
            if (!variants.TryGetValue(input.ProductVariantId, out var variant))
            {
                return Fail("One of the products on this receipt no longer exists.");
            }

            if (!variant.IsActive)
            {
                return Fail(
                    $"{variant.Sku} is inactive and cannot be received. Reactivate the variant first.");
            }

            if (input.Quantity <= 0m)
            {
                return Fail($"Enter a quantity greater than zero for {variant.Sku}.");
            }

            if (input.UnitCost < 0m)
            {
                return Fail($"The unit cost for {variant.Sku} cannot be negative.");
            }

            var lineTotal = decimal.Round(
                (input.Quantity * input.UnitCost) - input.DiscountAmount, 4, MidpointRounding.AwayFromZero);

            if (lineTotal < 0m)
            {
                return Fail($"The discount on {variant.Sku} is larger than the line itself.");
            }

            var batchNumber = Trim(input.BatchNumber);

            if (variant.IsBatchTracked && string.IsNullOrEmpty(batchNumber))
            {
                return Fail(
                    $"{variant.ProductName} is batch-tracked, so {variant.Sku} needs the supplier's "
                    + "batch number.");
            }

            var expiry = input.ExpiryDate;

            // A manufacture date plus a known shelf life is enough to work out
            // the expiry, so asking for it again would be asking twice.
            if (variant.IsExpiryTracked
                && expiry is null
                && input.ManufactureDate is not null
                && variant.ShelfLifeDays is > 0)
            {
                expiry = input.ManufactureDate.Value.AddDays(variant.ShelfLifeDays.Value);
            }

            if (variant.IsExpiryTracked && expiry is null)
            {
                return Fail(
                    $"{variant.ProductName} is expiry-tracked, so {variant.Sku} needs an expiry date "
                    + "(or a manufacture date, if the product has a shelf life set).");
            }

            if (expiry is not null && expiry <= receiptDate)
            {
                return Fail(
                    $"{variant.Sku} expires on {expiry:d MMM yyyy}, on or before the day it arrived. "
                    + "Check the date - receiving expired stock as sellable would put it on the shop.");
            }

            if (batchNumber is not null
                && !seenBatchKeys.Add($"{input.ProductVariantId}|{batchNumber}"))
            {
                return Fail(
                    $"Batch {batchNumber} appears twice for {variant.Sku} on this receipt. "
                    + "Combine those lines, or give them different batch numbers.");
            }

            plan.Lines.Add(new PlannedLine
            {
                ProductVariantId = variant.Id,
                Sku = variant.Sku,
                ProductName = variant.ProductName,
                Quantity = input.Quantity,
                UnitCost = input.UnitCost,
                DiscountAmount = input.DiscountAmount,
                LineTotal = lineTotal,
                LineTotalBase = decimal.Round(lineTotal * rate, 4, MidpointRounding.AwayFromZero),
                UnitWeightGrams = variant.WeightGrams,
                BatchNumber = batchNumber,
                ExpiryDate = expiry,
                ManufactureDate = input.ManufactureDate,
            });
        }

        // Charges belong to importing. Accepting them on a local purchase would
        // quietly inflate costs on a document with no place to show why.
        var charges = supplier.IsImporter
            ? request.Charges.Where(c => c.Amount != 0m).ToList()
            : [];

        if (charges.Any(c => c.Amount < 0m))
        {
            return Fail("A charge cannot be negative. Record a supplier credit separately.");
        }

        plan.Charges.AddRange(charges);

        var batchClash = await FindExistingBatchAsync(plan, cancellationToken);

        if (batchClash is not null)
        {
            return Fail(batchClash);
        }

        Cost(plan);

        return OperationResult<ReceiptPlan>.Success(plan);
    }

    /// <summary>
    /// Refuses a batch number already received for the same variant.
    ///
    /// A batch carries one landed cost and one expiry date. Receiving the same
    /// number twice at a different cost would mean either overwriting the first
    /// figure or keeping a number that no longer describes what is on the shelf.
    /// Refusing is blunt but leaves every stored value meaning exactly one
    /// thing, and the message says what to do instead.
    /// </summary>
    private async Task<string?> FindExistingBatchAsync(ReceiptPlan plan, CancellationToken cancellationToken)
    {
        var named = plan.Lines.Where(l => l.BatchNumber is not null).ToList();

        if (named.Count == 0)
        {
            return null;
        }

        var variantIds = named.Select(l => l.ProductVariantId).Distinct().ToList();
        var numbers = named.Select(l => l.BatchNumber!).Distinct().ToList();

        var existing = await _db.StockBatches
            .Where(b => variantIds.Contains(b.ProductVariantId) && numbers.Contains(b.BatchNumber))
            .Select(b => new { b.ProductVariantId, b.BatchNumber })
            .ToListAsync(cancellationToken);

        foreach (var line in named)
        {
            if (existing.Any(e => e.ProductVariantId == line.ProductVariantId
                                  && string.Equals(e.BatchNumber, line.BatchNumber, StringComparison.OrdinalIgnoreCase)))
            {
                return $"Batch {line.BatchNumber} has already been received for {line.Sku}. "
                       + "Each batch number is received once so its cost and expiry mean one thing - "
                       + "add a suffix if this is a second shipment of the same supplier batch.";
            }
        }

        return null;
    }

    private static void Cost(ReceiptPlan plan)
    {
        var apportionmentLines = plan.Lines
            .Select(l => new LandedCostApportionment.Line
            {
                Quantity = l.Quantity,
                ValueBase = l.LineTotalBase,
                UnitWeightGrams = l.UnitWeightGrams,
            })
            .ToList();

        var charges = plan.Charges
            .Select(c => new LandedCostApportionment.Charge
            {
                Amount = c.Amount,
                Method = c.ApportionMethod,
            })
            .ToList();

        plan.ChargeTotal = LandedCostApportionment.Apportion(apportionmentLines, charges);

        for (var i = 0; i < plan.Lines.Count; i++)
        {
            var line = plan.Lines[i];

            line.ApportionedCharge = apportionmentLines[i].ApportionedCharge;
            line.LandedUnitCost = LandedCostApportionment.UnitCost(
                line.LineTotalBase, line.ApportionedCharge, line.Quantity);
        }

        plan.SubTotal = plan.Lines.Sum(l => l.LineTotalBase);
    }

    // -----------------------------------------------------------------------
    // Writing - one transaction
    // -----------------------------------------------------------------------

    private async Task<OperationResult<long>> WriteAsync(ReceiptPlan plan, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var receipt = new GoodsReceipt
        {
            Number = await NextReceiptNumberAsync(plan.ReceiptDate, cancellationToken),
            SupplierId = plan.Supplier.Id,
            BranchId = plan.BranchId,
            WarehouseId = plan.WarehouseId,
            ReceiptDate = plan.ReceiptDate,
            Status = GoodsReceiptStatus.Posted,
            CurrencyCode = plan.CurrencyCode,
            ExchangeRate = plan.ExchangeRate,
            SupplierInvoiceNumber = plan.SupplierInvoiceNumber,
            SupplierInvoiceDate = plan.SupplierInvoiceDate,
            Notes = plan.Notes,
            SubTotal = plan.SubTotal,
            ChargeTotal = plan.ChargeTotal,
            GrandTotal = plan.SubTotal + plan.ChargeTotal,
            PostedAtUtc = now,
            PostedByUserId = _currentUser.UserId,
        };

        _db.GoodsReceipts.Add(receipt);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var charge in plan.Charges)
        {
            _db.PurchaseCharges.Add(new PurchaseCharge
            {
                GoodsReceiptId = receipt.Id,
                ChargeType = charge.ChargeType,
                Description = Trim(charge.Description),
                Amount = charge.Amount,
                ApportionMethod = charge.ApportionMethod,
            });
        }

        var lineNumber = 0;

        foreach (var line in plan.Lines)
        {
            lineNumber++;

            // Products nobody tracks batches on still get one - it is what the
            // ledger and any future recall hang off. Generated from the receipt
            // number so it is traceable, and flagged so the UI never shows it.
            var generated = line.BatchNumber is null;

            var batch = new StockBatch
            {
                ProductVariantId = line.ProductVariantId,
                BatchNumber = line.BatchNumber ?? $"{receipt.Number}-{lineNumber:D2}",
                IsAutoGenerated = generated,
                ExpiryDate = line.ExpiryDate,
                ManufactureDate = line.ManufactureDate,
                ReceivedDate = plan.ReceiptDate,
                LandedUnitCost = line.LandedUnitCost,
                SupplierId = plan.Supplier.Id,
                GoodsReceiptId = receipt.Id,
            };

            _db.StockBatches.Add(batch);
            await _db.SaveChangesAsync(cancellationToken);

            _db.GoodsReceiptLines.Add(new GoodsReceiptLine
            {
                GoodsReceiptId = receipt.Id,
                ProductVariantId = line.ProductVariantId,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                DiscountAmount = line.DiscountAmount,
                LineTotal = line.LineTotal,
                LineTotalBase = line.LineTotalBase,
                ApportionedCharge = line.ApportionedCharge,
                LandedUnitCost = line.LandedUnitCost,
                BatchNumber = batch.BatchNumber,
                ExpiryDate = line.ExpiryDate,
                ManufactureDate = line.ManufactureDate,
                StockBatchId = batch.Id,
            });

            // The ledger entry and the balance move together. Rule 1: no screen
            // updates a balance without writing the entry that justifies it.
            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductVariantId = line.ProductVariantId,
                WarehouseId = plan.WarehouseId,
                StockBatchId = batch.Id,
                MovementType = StockMovementType.Receipt,
                QuantityChange = line.Quantity,
                UnitCost = line.LandedUnitCost,
                ValueChange = decimal.Round(
                    line.Quantity * line.LandedUnitCost, 4, MidpointRounding.AwayFromZero),
                BranchId = plan.BranchId,
                DocumentType = StockDocumentType.GoodsReceipt,
                DocumentId = receipt.Id,
                DocumentNumber = receipt.Number,
                OccurredAtUtc = now,
                BusinessDate = plan.ReceiptDate,
            });

            // A batch is new here by construction - PrepareAsync refuses a
            // number that already exists - so this is always an insert. It is
            // written as an upsert anyway, because the next document type to
            // post into a balance will not have that guarantee.
            var balance = await _db.StockBalances.FirstOrDefaultAsync(
                b => b.ProductVariantId == line.ProductVariantId
                     && b.WarehouseId == plan.WarehouseId
                     && b.StockBatchId == batch.Id,
                cancellationToken);

            if (balance is null)
            {
                _db.StockBalances.Add(new StockBalance
                {
                    ProductVariantId = line.ProductVariantId,
                    WarehouseId = plan.WarehouseId,
                    StockBatchId = batch.Id,
                    QuantityOnHand = line.Quantity,
                    QuantityReserved = 0m,
                    LastMovementAtUtc = now,
                });
            }
            else
            {
                balance.QuantityOnHand += line.Quantity;
                balance.LastMovementAtUtc = now;
            }
        }

        await _audit.LogAsync(
            AuditActions.GoodsReceiptPosted,
            nameof(GoodsReceipt),
            receipt.Id.ToString(CultureInfo.InvariantCulture),
            $"Posted receipt {receipt.Number} from {plan.Supplier.Name}: "
            + $"{plan.Lines.Count} line(s), {plan.Lines.Sum(l => l.Quantity):N0} unit(s), "
            + $"{receipt.GrandTotal:N2} BDT.",
            new
            {
                receipt.Number,
                Supplier = plan.Supplier.Name,
                receipt.SubTotal,
                receipt.ChargeTotal,
                receipt.GrandTotal,
                Lines = plan.Lines.Select(l => new { l.Sku, l.Quantity, l.LandedUnitCost }),
            },
            plan.BranchId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(receipt.Id);
    }

    /// <summary>
    /// GRN-YYMM-0001, restarting each month. Read inside the posting
    /// transaction; see <see cref="DocumentNumber"/> for why that matters.
    /// </summary>
    private async Task<string> NextReceiptNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = DocumentNumber.Prefix(DocumentNumber.GoodsReceipt, date);

        var used = await _db.GoodsReceipts
            .Where(r => r.Number.StartsWith(prefix))
            .Select(r => r.Number)
            .ToListAsync(cancellationToken);

        return DocumentNumber.Next(prefix, used);
    }

    private static OperationResult<ReceiptPlan> Fail(string error, string? field = null) =>
        OperationResult<ReceiptPlan>.Failure(error, field);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // -----------------------------------------------------------------------
    // Everything decided before the first write
    // -----------------------------------------------------------------------

    private sealed class ReceiptPlan
    {
        public required Supplier Supplier { get; init; }

        public required long WarehouseId { get; init; }

        public required long BranchId { get; init; }

        public required DateOnly ReceiptDate { get; init; }

        public required string CurrencyCode { get; init; }

        public required decimal ExchangeRate { get; init; }

        public string? SupplierInvoiceNumber { get; init; }

        public DateOnly? SupplierInvoiceDate { get; init; }

        public string? Notes { get; init; }

        public List<PlannedLine> Lines { get; } = [];

        public List<ReceiptChargeInput> Charges { get; } = [];

        public decimal SubTotal { get; set; }

        public decimal ChargeTotal { get; set; }
    }

    private sealed class PlannedLine
    {
        public required long ProductVariantId { get; init; }

        public required string Sku { get; init; }

        public required string ProductName { get; init; }

        public required decimal Quantity { get; init; }

        public required decimal UnitCost { get; init; }

        public required decimal DiscountAmount { get; init; }

        public required decimal LineTotal { get; init; }

        public required decimal LineTotalBase { get; init; }

        public decimal? UnitWeightGrams { get; init; }

        public string? BatchNumber { get; init; }

        public DateOnly? ExpiryDate { get; init; }

        public DateOnly? ManufactureDate { get; init; }

        public decimal ApportionedCharge { get; set; }

        public decimal LandedUnitCost { get; set; }
    }
}
