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

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
