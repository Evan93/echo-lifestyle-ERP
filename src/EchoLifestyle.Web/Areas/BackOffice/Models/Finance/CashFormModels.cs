using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Finance.Cash;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Finance;

/// <summary>
/// Recording one movement of money.
///
/// There is no direction field. The kind decides which way the money went, so
/// the form cannot express "an expense that increased our cash" - which is a
/// sentence nobody means and everybody occasionally types.
/// </summary>
public class RecordCashFormModel
{
    [Display(Name = "What is this?")]
    public CashKind Kind { get; set; } = CashKind.Expense;

    [Display(Name = "Paid by")]
    public PaymentMethodKind Method { get; set; } = PaymentMethodKind.Cash;

    [Range(0.01, 99999999, ErrorMessage = "How much was it?")]
    [Display(Name = "Amount")]
    public decimal Amount { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date")]
    public DateOnly? TransactionDate { get; set; }

    [Display(Name = "Branch")]
    public long BranchId { get; set; }

    [Display(Name = "Category")]
    public long? ExpenseCategoryId { get; set; }

    [Display(Name = "Partner")]
    public long? PartnerId { get; set; }

    [Display(Name = "Supplier")]
    public long? SupplierId { get; set; }

    [StringLength(40)]
    [Display(Name = "Order number")]
    public string? OrderNumber { get; set; }

    [StringLength(100)]
    [Display(Name = "Reference")]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    public IReadOnlyList<ExpenseCategoryOption> Categories { get; set; } = [];

    public IReadOnlyList<PartnerOption> Partners { get; set; } = [];

    public IReadOnlyList<SupplierOption> Suppliers { get; set; } = [];

    /// <summary>
    /// Whether this user may write capital and drawings. Partner money is not
    /// every member of staff's business, and the option is removed rather than
    /// disabled - a greyed-out box still tells you the figure exists.
    /// </summary>
    public bool CanRecordPartnerMoney { get; set; }

    public RecordCashRequest ToRequest() => new()
    {
        Kind = Kind,
        Method = Method,
        Amount = Amount,
        TransactionDate = TransactionDate,
        BranchId = BranchId,

        // Only the fields the chosen kind actually uses are sent. The browser
        // keeps whatever was typed into a box that was later hidden, and the
        // service refuses a category on a supplier payment - correctly, and
        // confusingly, unless the form clears them here.
        ExpenseCategoryId = Kind == CashKind.Expense ? ExpenseCategoryId : null,
        PartnerId = CashTransaction.RequiresPartner(Kind) ? PartnerId : null,
        SupplierId = Kind == CashKind.SupplierPayment ? SupplierId : null,
        OrderNumber = CashTransaction.RequiresSalesOrder(Kind) ? OrderNumber : null,
        ReferenceNumber = ReferenceNumber,
        Notes = Notes,
    };
}

public class ExpenseCategoryFormModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Give the category a name.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "What belongs here")]
    public string? Description { get; set; }

    [Display(Name = "Rises with sales")]
    public bool IsCostOfSale { get; set; }

    [Display(Name = "In use")]
    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public SaveExpenseCategoryRequest ToRequest() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        IsCostOfSale = IsCostOfSale,
        IsActive = IsActive,
        DisplayOrder = DisplayOrder,
    };
}

public class PartnerFormModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "What is the partner called?")]
    [StringLength(150)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100)]
    [Display(Name = "Share (%)")]
    public decimal? OwnershipPercent { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public SavePartnerRequest ToRequest() => new()
    {
        Id = Id,
        Name = Name,
        OwnershipPercent = OwnershipPercent,
        IsActive = IsActive,
        Notes = Notes,
    };
}
