using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Purchasing.Suppliers;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

public class SupplierFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [StringLength(20, MinimumLength = 2)]
    [Display(Name = "Supplier code")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Enter a supplier name.")]
    [StringLength(200)]
    [Display(Name = "Supplier name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Contact person")]
    public string? ContactName { get; set; }

    [StringLength(40)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [StringLength(200)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(250)]
    [Display(Name = "Address")]
    public string? AddressLine1 { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(100)]
    [Display(Name = "Country")]
    public string? Country { get; set; }

    [Display(Name = "Import source")]
    public bool IsImporter { get; set; }

    [StringLength(3, MinimumLength = 3)]
    [Display(Name = "Invoice currency")]
    public string CurrencyCode { get; set; } = "BDT";

    [Range(0, 365)]
    [Display(Name = "Payment terms (days)")]
    public int PaymentTermDays { get; set; }

    [StringLength(50)]
    [Display(Name = "BIN")]
    public string? Bin { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Read-only: decides whether Delete is offered.</summary>
    public int BatchCount { get; set; }

    public SaveSupplierRequest ToRequest() => new()
    {
        Code = Code,
        Name = Name,
        ContactName = ContactName,
        Phone = Phone,
        Email = Email,
        AddressLine1 = AddressLine1,
        City = City,
        Country = Country,
        IsImporter = IsImporter,
        CurrencyCode = CurrencyCode,
        PaymentTermDays = PaymentTermDays,
        Bin = Bin,
        Notes = Notes,
        IsActive = IsActive,
    };

    public static SupplierFormModel FromDetail(SupplierDetail detail) => new()
    {
        Id = detail.Id,
        Code = detail.Code,
        Name = detail.Name,
        ContactName = detail.ContactName,
        Phone = detail.Phone,
        Email = detail.Email,
        AddressLine1 = detail.AddressLine1,
        City = detail.City,
        Country = detail.Country,
        IsImporter = detail.IsImporter,
        CurrencyCode = detail.CurrencyCode,
        PaymentTermDays = detail.PaymentTermDays,
        Bin = detail.Bin,
        Notes = detail.Notes,
        IsActive = detail.IsActive,
        BatchCount = detail.BatchCount,
    };
}
