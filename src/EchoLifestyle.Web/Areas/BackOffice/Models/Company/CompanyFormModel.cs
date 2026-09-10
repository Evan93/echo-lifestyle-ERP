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
    /// Shown in the storefront footer and on the contact page. Anything that is
    /// not an absolute http(s) URL is dropped when it is saved rather than
    /// stored - these end up in an href on every public page.
    /// </summary>
    [Url(ErrorMessage = "Enter the full address, starting with https://")]
    [StringLength(300)]
    [Display(Name = "Facebook page URL")]
    public string? FacebookUrl { get; set; }

    [Url(ErrorMessage = "Enter the full address, starting with https://")]
    [StringLength(300)]
    [Display(Name = "Instagram profile URL")]
    public string? InstagramUrl { get; set; }

    /// <summary>
    /// Leave both blank on a development machine. Nobody wants localhost
    /// traffic in the conversion figures the ad budget is set from.
    /// </summary>
    [RegularExpression(@"^\d{8,20}$", ErrorMessage = "A pixel id is digits only.")]
    [StringLength(30)]
    [Display(Name = "Meta pixel ID")]
    public string? MetaPixelId { get; set; }

    [RegularExpression(@"^G-[A-Za-z0-9]{4,20}$",
        ErrorMessage = "A GA4 measurement id looks like G-XXXXXXXXXX.")]
    [StringLength(30)]
    [Display(Name = "Google Analytics ID")]
    public string? GoogleAnalyticsId { get; set; }

    /// <summary>
    /// Read-only. Changing the base currency once transactions exist is a data
    /// migration, not a setting - every stored amount would have to be restated.
    /// </summary>
    public string BaseCurrencyCode { get; set; } = "BDT";

    public string BusinessTimeZoneId { get; set; } = "Asia/Dhaka";

    [Range(0, 99999, ErrorMessage = "Enter a delivery charge of zero or more.")]
    [Display(Name = "Delivery inside Dhaka city")]
    public decimal DeliveryChargeInsideCity { get; set; }

    [Range(0, 99999, ErrorMessage = "Enter a delivery charge of zero or more.")]
    [Display(Name = "Delivery elsewhere")]
    public decimal DeliveryChargeOutsideCity { get; set; }

    [Range(0, 9999999, ErrorMessage = "Enter an order value, or leave it blank.")]
    [Display(Name = "Free delivery over")]
    public decimal? FreeDeliveryOverAmount { get; set; }

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
        FacebookUrl = FacebookUrl,
        InstagramUrl = InstagramUrl,
        MetaPixelId = MetaPixelId,
        GoogleAnalyticsId = GoogleAnalyticsId,
        DeliveryChargeInsideCity = DeliveryChargeInsideCity,
        DeliveryChargeOutsideCity = DeliveryChargeOutsideCity,
        FreeDeliveryOverAmount = FreeDeliveryOverAmount,
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
        FacebookUrl = detail.FacebookUrl,
        InstagramUrl = detail.InstagramUrl,
        MetaPixelId = detail.MetaPixelId,
        GoogleAnalyticsId = detail.GoogleAnalyticsId,
        BaseCurrencyCode = detail.BaseCurrencyCode,
        BusinessTimeZoneId = detail.BusinessTimeZoneId,
        DeliveryChargeInsideCity = detail.DeliveryChargeInsideCity,
        DeliveryChargeOutsideCity = detail.DeliveryChargeOutsideCity,
        FreeDeliveryOverAmount = detail.FreeDeliveryOverAmount,
    };
}
