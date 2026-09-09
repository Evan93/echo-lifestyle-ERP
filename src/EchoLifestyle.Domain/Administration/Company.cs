using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Administration;

/// <summary>
/// The operating company. Single entity today; the model does not assume
/// only one row so future expansion does not require a migration rewrite.
/// </summary>
public class Company : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public string? LegalName { get; set; }

    /// <summary>
    /// NBR VAT registration (BIN). Null until the business registers.
    /// Tax features stay dormant while this is null - see Accounting-Rules.md.
    /// </summary>
    public string? VatRegistrationNumber { get; set; }

    public string? TradeLicenseNumber { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }

    public string CountryCode { get; set; } = "BD";

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>ISO 4217 code of the base/reporting currency.</summary>
    public string BaseCurrencyCode { get; set; } = "BDT";

    /// <summary>IANA time zone used to derive the business date from UTC.</summary>
    public string BusinessTimeZoneId { get; set; } = "Asia/Dhaka";

    /// <summary>
    /// What the website charges to deliver inside Dhaka city, and everywhere
    /// else. Two rates rather than sixty-four, because that is what couriers
    /// here actually price on and what every Bangladeshi shop quotes.
    ///
    /// Configuration, not code: the courier changes these and so will you. The
    /// seeded figures are placeholders until somebody enters the real ones.
    /// </summary>
    public decimal DeliveryChargeInsideCity { get; set; } = 60m;

    public decimal DeliveryChargeOutsideCity { get; set; } = 120m;

    /// <summary>
    /// Order value above which delivery is free. Null switches the offer off,
    /// which is the state to leave it in until somebody has decided the
    /// threshold is worth the margin it costs.
    /// </summary>
    public decimal? FreeDeliveryOverAmount { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
