using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Administration.CompanyProfile;

/// <summary>
/// The company profile: the details that print on documents, and the NBR
/// registration that switches VAT on.
/// </summary>
public partial class CompanyAdminService
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
                FacebookUrl = c.FacebookUrl,
                InstagramUrl = c.InstagramUrl,
                MetaPixelId = c.MetaPixelId,
                GoogleAnalyticsId = c.GoogleAnalyticsId,
                BaseCurrencyCode = c.BaseCurrencyCode,
                BusinessTimeZoneId = c.BusinessTimeZoneId,
                DeliveryChargeInsideCity = c.DeliveryChargeInsideCity,
                DeliveryChargeOutsideCity = c.DeliveryChargeOutsideCity,
                FreeDeliveryOverAmount = c.FreeDeliveryOverAmount,
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

        // Anything that is not an http(s) URL is dropped rather than stored.
        // These end up in an href on a public page, and "javascript:" in a
        // link the whole site renders is not a setting anybody needs.
        company.FacebookUrl = WebLink(request.FacebookUrl);
        company.InstagramUrl = WebLink(request.InstagramUrl);

        // Strict, and silent about it: an id that does not match the shape the
        // vendor issues is not stored at all. See the note on TrackingId.
        company.MetaPixelId = TrackingId(request.MetaPixelId, MetaPixelPattern());
        company.GoogleAnalyticsId = TrackingId(request.GoogleAnalyticsId, GoogleAnalyticsPattern());

        // Negatives would quietly discount the order rather than charge for
        // delivery, so they are floored rather than trusted.
        company.DeliveryChargeInsideCity = Math.Max(0m, request.DeliveryChargeInsideCity);
        company.DeliveryChargeOutsideCity = Math.Max(0m, request.DeliveryChargeOutsideCity);
        company.FreeDeliveryOverAmount = request.FreeDeliveryOverAmount is > 0m
            ? request.FreeDeliveryOverAmount
            : null;

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

    /// <summary>
    /// An absolute http or https URL, or null.
    ///
    /// Validated here rather than in the view because the view is not the only
    /// caller and because this value is rendered into an <c>href</c> on every
    /// public page. Razor escapes the text, which stops the tag being broken
    /// out of, but it will happily emit a "javascript:" href intact - that is
    /// a scheme check's job, not an encoder's.
    /// </summary>
    private static string? WebLink(string? value)
    {
        var trimmed = Trim(value);

        if (trimmed is null)
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }

    /// <summary>
    /// A tracking id that matches the shape its vendor issues, or null.
    ///
    /// This is the strictest validation in the file, and it is the one that
    /// most needs to be. Both ids are rendered <em>inside a script tag</em> on
    /// every public page, where HTML encoding does nothing useful: a value
    /// containing a quote and a semicolon would not break out of the tag, it
    /// would simply become the next statement in it. Allowing only the
    /// characters the real ids are made of removes the question entirely.
    ///
    /// Silently dropping a malformed id rather than refusing the save is
    /// deliberate. The rest of the company profile - the address that prints on
    /// invoices, the delivery charges - must not be held hostage by a mistyped
    /// analytics id, and the field showing empty afterwards is a clear enough
    /// signal that it did not take.
    /// </summary>
    private static string? TrackingId(string? value, System.Text.RegularExpressions.Regex pattern)
    {
        var trimmed = Trim(value);

        return trimmed is not null && pattern.IsMatch(trimmed) ? trimmed : null;
    }

    /// <summary>Meta issues numeric pixel ids, currently fifteen or sixteen digits.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{8,20}$")]
    private static partial System.Text.RegularExpressions.Regex MetaPixelPattern();

    /// <summary>GA4 measurement ids are "G-" followed by an alphanumeric stream.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^G-[A-Za-z0-9]{4,20}$")]
    private static partial System.Text.RegularExpressions.Regex GoogleAnalyticsPattern();
}
