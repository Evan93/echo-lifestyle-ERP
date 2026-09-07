using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Sales;

/// <summary>
/// One screen of taking an order.
///
/// Lines are appended by searching, the same way a purchase is received. The
/// difference is that every result shows what can actually be promised - on hand
/// minus what other orders are already holding - because offering stock that is
/// in somebody else's box is how a customer gets told twice.
/// </summary>
public class OrderFormModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Choose a customer.")]
    [Display(Name = "Customer")]
    public long CustomerId { get; set; }

    /// <summary>Posted back so a failed save redraws the picker without another lookup.</summary>
    public string? CustomerName { get; set; }

    [Display(Name = "Deliver to")]
    public long? CustomerAddressId { get; set; }

    [Display(Name = "Branch")]
    public long BranchId { get; set; }

    [Required(ErrorMessage = "Choose a warehouse to ship from.")]
    [Display(Name = "Ship from")]
    public long WarehouseId { get; set; }

    [Display(Name = "Order date")]
    [DataType(DataType.Date)]
    public DateOnly? OrderDate { get; set; }

    [Display(Name = "Came in via")]
    public SalesChannel Channel { get; set; } = SalesChannel.Messenger;

    [Display(Name = "Payment")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;

    [Range(0, 99999999)]
    [Display(Name = "Order discount")]
    public decimal DiscountAmount { get; set; }

    [Range(0, 99999999)]
    [Display(Name = "Delivery charge")]
    public decimal DeliveryCharge { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public List<OrderLineRow> Lines { get; set; } = [];

    public IReadOnlyList<WarehouseOption> Warehouses { get; set; } = [];

    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    /// <summary>The chosen customer's addresses, loaded once they are picked.</summary>
    public IReadOnlyList<CustomerAddressItem> Addresses { get; set; } = [];

    public bool IsEdit => Id > 0;

    public SaveOrderRequest ToRequest() => new()
    {
        CustomerId = CustomerId,
        CustomerAddressId = CustomerAddressId,
        BranchId = BranchId,
        WarehouseId = WarehouseId,
        OrderDate = OrderDate,
        Channel = Channel,
        PaymentMethod = PaymentMethod,
        DiscountAmount = DiscountAmount,
        DeliveryCharge = DeliveryCharge,
        Notes = Notes,
        Lines = Lines
            .Where(l => l.ProductVariantId > 0)
            .Select(l => new OrderLineInput
            {
                ProductVariantId = l.ProductVariantId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountAmount = l.DiscountAmount,
                Notes = l.Notes,
            })
            .ToList(),
    };

    public static OrderFormModel From(SalesOrderDetail detail) => new()
    {
        Id = detail.Id,
        CustomerId = detail.CustomerId,
        CustomerName = detail.CustomerName,
        BranchId = detail.BranchId,
        WarehouseId = detail.WarehouseId,
        OrderDate = detail.OrderDate,
        Channel = detail.Channel,
        PaymentMethod = detail.PaymentMethod,
        DiscountAmount = detail.DiscountAmount,
        DeliveryCharge = detail.DeliveryCharge,
        Notes = detail.Notes,
        Lines = detail.Lines
            .Select(l => new OrderLineRow
            {
                ProductVariantId = l.ProductVariantId,
                Sku = l.Sku,
                Description = l.VariantName == "Standard"
                    ? l.ProductName
                    : $"{l.ProductName} - {l.VariantName}",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                DiscountAmount = l.DiscountAmount,
                Notes = l.Notes,
            })
            .ToList(),
    };
}

public class OrderLineRow
{
    public long ProductVariantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Range(0.0001, 9999999)]
    public decimal Quantity { get; set; } = 1m;

    /// <summary>
    /// Null takes the price list's answer. Typed, it overrides - somebody agreed
    /// a figure in a conversation and the record should say what was agreed.
    /// </summary>
    [Range(0, 99999999)]
    public decimal? UnitPrice { get; set; }

    [Range(0, 99999999)]
    public decimal DiscountAmount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class DispatchFormModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Name the courier.")]
    [StringLength(100)]
    [Display(Name = "Courier")]
    public string CourierName { get; set; } = string.Empty;

    [StringLength(60)]
    [Display(Name = "Consignment number")]
    public string? ConsignmentNumber { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public class DeliveryFormModel
{
    public long Id { get; set; }

    /// <summary>Blank means the full amount came back.</summary>
    [Range(0, 99999999)]
    [Display(Name = "Collected")]
    public decimal? AmountCollected { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

/// <summary>Ending an order badly. Both endings need a reason.</summary>
public class OrderReasonModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Say why.")]
    [StringLength(500, MinimumLength = 3)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;
}
