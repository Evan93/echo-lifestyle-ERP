using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Administration.CompanyProfile;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Company;

public class CompanyFormModel
{
    [Required(ErrorMessage = "Enter the company name.")]
    [StringLength(200)]
    [Display(Name = "Trading name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Legal name")]
    public string? LegalName { get; set; }

    [StringLength(50)]
    [Display(Name = "VAT registration (BIN)")]
    public string? VatRegistrationNumber { get; set; }

    [StringLength(50)]
    [Display(Name = "Trade licence number")]
    public string? TradeLicenseNumber { get; set; }

    [StringLength(250)]
    [Display(Name = "Address line 1")]
    public string? AddressLine1 { get; set; }

    [StringLength(250)]
    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(20)]
    [Display(Name = "Postal code")]
    public string? PostalCode { get; set; }

    [StringLength(50)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(250)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    /// <summary>
    /// Read-only. Changing the base currency once transactions exist is a data
    /// migration, not a setting - every stored amount would have to be restated.
    /// </summary>
    public string BaseCurrencyCode { get; set; } = "BDT";

    public string BusinessTimeZoneId { get; set; } = "Asia/Dhaka";

    public bool IsVatRegistered => !string.IsNullOrWhiteSpace(VatRegistrationNumber);

    public SaveCompanyRequest ToRequest() => new()
    {
        Name = Name,
        LegalName = LegalName,
        VatRegistrationNumber = VatRegistrationNumber,
        TradeLicenseNumber = TradeLicenseNumber,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        PostalCode = PostalCode,
        Phone = Phone,
        Email = Email,
    };

    public static CompanyFormModel FromDetail(CompanyDetail detail) => new()
    {
        Name = detail.Name,
        LegalName = detail.LegalName,
        VatRegistrationNumber = detail.VatRegistrationNumber,
        TradeLicenseNumber = detail.TradeLicenseNumber,
        AddressLine1 = detail.AddressLine1,
        AddressLine2 = detail.AddressLine2,
        City = detail.City,
        PostalCode = detail.PostalCode,
        Phone = detail.Phone,
        Email = detail.Email,
        BaseCurrencyCode = detail.BaseCurrencyCode,
        BusinessTimeZoneId = detail.BusinessTimeZoneId,
    };
}
