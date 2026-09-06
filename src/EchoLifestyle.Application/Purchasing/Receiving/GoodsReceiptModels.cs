using EchoLifestyle.Domain.Purchasing;

namespace EchoLifestyle.Application.Purchasing.Receiving;

/// <summary>
/// One screen's worth of receiving: who it came from, where it went, and what
/// was in it.
/// </summary>
public class QuickPurchaseRequest
{
    public long SupplierId { get; set; }

    public long WarehouseId { get; set; }

    public long BranchId { get; set; }

    /// <summary>Defaults to today's business date when not given.</summary>
    public DateOnly? ReceiptDate { get; set; }

    public string? SupplierInvoiceNumber { get; set; }

    public DateOnly? SupplierInvoiceDate { get; set; }

    /// <summary>
    /// Only meaningful for an import source. A local purchase is always BDT at
    /// a rate of 1, whatever is posted.
    /// </summary>
    public decimal? ExchangeRate { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyList<ReceiptLineInput> Lines { get; set; } = [];

    public IReadOnlyList<ReceiptChargeInput> Charges { get; set; } = [];
}

public class ReceiptLineInput
{
    public long ProductVariantId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>In the supplier's currency.</summary>
    public decimal UnitCost { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Required only when the product is batch-tracked. Left blank on anything
    /// else, one is generated and never shown.
    /// </summary>
    public string? BatchNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public DateOnly? ManufactureDate { get; set; }
}

public class ReceiptChargeInput
{
    public PurchaseChargeType ChargeType { get; set; }

    public string? Description { get; set; }

    /// <summary>In BDT.</summary>
    public decimal Amount { get; set; }

    public ChargeApportionMethod ApportionMethod { get; set; } = ChargeApportionMethod.ByValue;
}

public class GoodsReceiptListItem
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string WarehouseName { get; set; } = string.Empty;

    public DateOnly ReceiptDate { get; set; }

    public GoodsReceiptStatus Status { get; set; }

    public int LineCount { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal GrandTotal { get; set; }

    public string? SupplierInvoiceNumber { get; set; }
}

public class GoodsReceiptDetail
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public long SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public bool SupplierIsImporter { get; set; }

    public long WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;

    public DateOnly ReceiptDate { get; set; }

    public GoodsReceiptStatus Status { get; set; }

    public string CurrencyCode { get; set; } = "BDT";

    public decimal ExchangeRate { get; set; }

    public string? SupplierInvoiceNumber { get; set; }

    public DateOnly? SupplierInvoiceDate { get; set; }

    public string? Notes { get; set; }

    public decimal SubTotal { get; set; }

    public decimal ChargeTotal { get; set; }

    public decimal GrandTotal { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public IReadOnlyList<GoodsReceiptLineDetail> Lines { get; set; } = [];

    public IReadOnlyList<GoodsReceiptChargeDetail> Charges { get; set; } = [];
}

public class GoodsReceiptLineDetail
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal LineTotal { get; set; }

    public decimal LineTotalBase { get; set; }

    public decimal ApportionedCharge { get; set; }

    public decimal LandedUnitCost { get; set; }

    /// <summary>Hidden in the UI when the batch was generated rather than typed.</summary>
    public string? BatchNumber { get; set; }

    public bool BatchWasGenerated { get; set; }

    public DateOnly? ExpiryDate { get; set; }
}

public class GoodsReceiptChargeDetail
{
    public PurchaseChargeType ChargeType { get; set; }

    public string? Description { get; set; }

    public decimal Amount { get; set; }

    public ChargeApportionMethod ApportionMethod { get; set; }
}
