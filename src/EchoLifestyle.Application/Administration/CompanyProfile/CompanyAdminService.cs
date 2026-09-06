using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Administration.CompanyProfile;

/// <summary>
/// The company profile: the details that print on documents, and the NBR
/// registration that switches VAT on.
/// </summary>
public class CompanyAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public CompanyAdminService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<CompanyDetail?> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Companies
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CompanyDetail
            {
                Id = c.Id,
                Name = c.Name,
                LegalName = c.LegalName,
                VatRegistrationNumber = c.VatRegistrationNumber,
                TradeLicenseNumber = c.TradeLicenseNumber,
                AddressLine1 = c.AddressLine1,
                AddressLine2 = c.AddressLine2,
                City = c.City,
                PostalCode = c.PostalCode,
                Phone = c.Phone,
                Email = c.Email,
                BaseCurrencyCode = c.BaseCurrencyCode,
                BusinessTimeZoneId = c.BusinessTimeZoneId,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OperationResult> UpdateAsync(
        SaveCompanyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult.Failure("Enter the company name.", nameof(SaveCompanyRequest.Name));
        }

        var company = await _db.Companies.OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            return OperationResult.Failure("No company record exists.");
        }

        var vat = Trim(request.VatRegistrationNumber);

        // Deliberately a light check. The exact BIN format is an NBR rule and
        // this codebase does not invent tax rules - a wrong format check would
        // block a valid number. Length and character sanity only; confirm the
        // real format before relying on it.
        if (vat is not null && (vat.Length < 6 || vat.Length > 50))
        {
            return OperationResult.Failure(
                "That does not look like a VAT registration number.",
                nameof(SaveCompanyRequest.VatRegistrationNumber));
        }

        var wasRegistered = !string.IsNullOrWhiteSpace(company.VatRegistrationNumber);
        var before = new
        {
            company.Name,
            company.LegalName,
            company.VatRegistrationNumber,
            company.TradeLicenseNumber,
        };

        company.Name = request.Name.Trim();
        company.LegalName = Trim(request.LegalName);
        company.VatRegistrationNumber = vat;
        company.TradeLicenseNumber = Trim(request.TradeLicenseNumber);
        company.AddressLine1 = Trim(request.AddressLine1);
        company.AddressLine2 = Trim(request.AddressLine2);
        company.City = Trim(request.City);
        company.PostalCode = Trim(request.PostalCode);
        company.Phone = Trim(request.Phone);
        company.Email = Trim(request.Email);

        await _audit.LogAsync(
            AuditActions.CompanyUpdated,
            nameof(Company),
            company.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Updated the company profile.",
            new { Before = before, After = new { company.Name, company.LegalName, company.VatRegistrationNumber, company.TradeLicenseNumber } },
            cancellationToken: cancellationToken);

        // Registering for VAT changes how every invoice is produced from that
        // point on. It gets its own audit entry so the date it took effect is
        // findable without reading through profile edits.
        var isRegistered = !string.IsNullOrWhiteSpace(vat);

        if (wasRegistered != isRegistered)
        {
            await _audit.LogAsync(
                isRegistered
                    ? "Administration.Company.VatRegistered"
                    : "Administration.Company.VatRegistrationRemoved",
                nameof(Company),
                company.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                isRegistered
                    ? $"VAT registration recorded ({vat}). Mushak invoicing is now active."
                    : "VAT registration removed. Tax features are dormant again.",
                new { VatRegistrationNumber = vat },
                cancellationToken: cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
