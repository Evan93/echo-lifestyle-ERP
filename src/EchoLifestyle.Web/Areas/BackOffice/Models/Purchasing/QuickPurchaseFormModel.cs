using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Domain.Purchasing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

/// <summary>
/// One screen of receiving.
///
/// Lines arrive from the browser as an indexed collection, appended by the
/// search box rather than typed into a fixed grid: the fast path is scan or
/// type an SKU, press Enter, correct the quantity. Nothing here is trusted -
/// the service revalidates every field and recomputes every total.
/// </summary>
public class QuickPurchaseFormModel
{
    [Required(ErrorMessage = "Choose a supplier.")]
    [Display(Name = "Supplier")]
    public long SupplierId { get; set; }

    [Required(ErrorMessage = "Choose a warehouse.")]
    [Display(Name = "Receive into")]
    public long WarehouseId { get; set; }

    [Display(Name = "Branch")]
    public long BranchId { get; set; }

    [Display(Name = "Received on")]
    [DataType(DataType.Date)]
    public DateOnly? ReceiptDate { get; set; }

    [StringLength(60)]
    [Display(Name = "Supplier invoice no.")]
    public string? SupplierInvoiceNumber { get; set; }

    [Display(Name = "Invoice date")]
    [DataType(DataType.Date)]
    public DateOnly? SupplierInvoiceDate { get; set; }

    [Range(0.000001, 100000)]
    [Display(Name = "Exchange rate")]
    public decimal? ExchangeRate { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public List<ReceiptLineRow> Lines { get; set; } = [];

    public List<ReceiptChargeRow> Charges { get; set; } = [];

    public IReadOnlyList<SupplierOption> Suppliers { get; set; } = [];

    public IReadOnlyList<WarehouseOption> Warehouses { get; set; } = [];

    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    public QuickPurchaseRequest ToRequest() => new()
    {
        SupplierId = SupplierId,
        WarehouseId = WarehouseId,
        BranchId = BranchId,
        ReceiptDate = ReceiptDate,
        SupplierInvoiceNumber = SupplierInvoiceNumber,
        SupplierInvoiceDate = SupplierInvoiceDate,
        ExchangeRate = ExchangeRate,
        Notes = Notes,
        Lines = Lines
            .Where(l => l.ProductVariantId > 0)
            .Select(l => new ReceiptLineInput
            {
                ProductVariantId = l.ProductVariantId,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost,
                DiscountAmount = l.DiscountAmount,
                BatchNumber = l.BatchNumber,
                ExpiryDate = l.ExpiryDate,
                ManufactureDate = l.ManufactureDate,
            })
            .ToList(),
        Charges = Charges
            .Where(c => c.Amount > 0m)
            .Select(c => new ReceiptChargeInput
            {
                ChargeType = c.ChargeType,
                Description = c.Description,
                Amount = c.Amount,
                ApportionMethod = c.ApportionMethod,
            })
            .ToList(),
    };
}

public class ReceiptLineRow
{
    public long ProductVariantId { get; set; }

    /// <summary>Posted back so a failed save can redraw the line without another lookup.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsBatchTracked { get; set; }

    public bool IsExpiryTracked { get; set; }

    [Range(0.0001, 9999999)]
    public decimal Quantity { get; set; } = 1m;

    [Range(0, 99999999)]
    public decimal UnitCost { get; set; }

    [Range(0, 99999999)]
    public decimal DiscountAmount { get; set; }

    [StringLength(60)]
    public string? BatchNumber { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? ExpiryDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? ManufactureDate { get; set; }
}

public class ReceiptChargeRow
{
    public PurchaseChargeType ChargeType { get; set; } = PurchaseChargeType.Freight;

    [StringLength(200)]
    public string? Description { get; set; }

    [Range(0, 99999999)]
    public decimal Amount { get; set; }

    public ChargeApportionMethod ApportionMethod { get; set; } = ChargeApportionMethod.ByValue;
}

public class BranchOption
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
