namespace EchoLifestyle.Application.Administration.CompanyProfile;

public class CompanyDetail
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? LegalName { get; init; }

    public string? VatRegistrationNumber { get; init; }

    public string? TradeLicenseNumber { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public string BaseCurrencyCode { get; init; } = "BDT";

    public string BusinessTimeZoneId { get; init; } = "Asia/Dhaka";

    /// <summary>
    /// VAT features stay dormant until an NBR registration number is recorded.
    /// Once it is, invoices switch to the Mushak-compliant layout and tax
    /// reporting becomes available.
    /// </summary>
    public bool IsVatRegistered => !string.IsNullOrWhiteSpace(VatRegistrationNumber);
}

public class SaveCompanyRequest
{
    public string Name { get; set; } = string.Empty;

    public string? LegalName { get; set; }

    public string? VatRegistrationNumber { get; set; }

    public string? TradeLicenseNumber { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }
}
