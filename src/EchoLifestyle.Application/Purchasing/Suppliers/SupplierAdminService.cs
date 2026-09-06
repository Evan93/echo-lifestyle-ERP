using System.Globalization;
using System.Text.RegularExpressions;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Purchasing.Suppliers;

/// <summary>
/// Supplier administration.
///
/// Suppliers are company-wide, not branch-scoped: the same distributor serves
/// every branch, and duplicating them per branch would make "what did we buy
/// from them this year" unanswerable.
/// </summary>
public partial class SupplierAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public SupplierAdminService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]{1,19}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    public async Task<PagedResult<SupplierListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Suppliers.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(s => s.IsActive),
            StatusFilter.Inactive => query.Where(s => !s.IsActive),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                EF.Functions.Like(s.Code, $"%{term}%")
                || EF.Functions.Like(s.Name, $"%{term}%")
                || (s.ContactName != null && EF.Functions.Like(s.ContactName, $"%{term}%"))
                || (s.Phone != null && EF.Functions.Like(s.Phone, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("name", false) => query.OrderBy(s => s.Name),
            ("name", true) => query.OrderByDescending(s => s.Name),
            ("code", true) => query.OrderByDescending(s => s.Code),
            ("city", false) => query.OrderBy(s => s.City).ThenBy(s => s.Name),
            ("city", true) => query.OrderByDescending(s => s.City).ThenBy(s => s.Name),
            ("terms", false) => query.OrderBy(s => s.PaymentTermDays).ThenBy(s => s.Name),
            ("terms", true) => query.OrderByDescending(s => s.PaymentTermDays).ThenBy(s => s.Name),
            ("isActive", false) => query.OrderBy(s => s.IsActive).ThenBy(s => s.Name),
            ("isActive", true) => query.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name),
            _ => query.OrderBy(s => s.Code),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(s => new SupplierListItem
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                ContactName = s.ContactName,
                Phone = s.Phone,
                City = s.City,
                IsImporter = s.IsImporter,
                CurrencyCode = s.CurrencyCode,
                PaymentTermDays = s.PaymentTermDays,
                IsActive = s.IsActive,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierListItem>(rows, totalCount, filteredCount);
    }

    public async Task<SupplierDetail?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        await _db.Suppliers
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SupplierDetail
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                ContactName = s.ContactName,
                Phone = s.Phone,
                Email = s.Email,
                AddressLine1 = s.AddressLine1,
                City = s.City,
                Country = s.Country,
                IsImporter = s.IsImporter,
                CurrencyCode = s.CurrencyCode,
                PaymentTermDays = s.PaymentTermDays,
                Bin = s.Bin,
                Notes = s.Notes,
                IsActive = s.IsActive,
                BatchCount = _db.StockBatches.Count(b => b.SupplierId == s.Id),
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Active suppliers for the purchase screens' dropdown.</summary>
    public async Task<IReadOnlyList<SupplierOption>> GetOptionsAsync(
        long? includeInactiveId = null,
        CancellationToken cancellationToken = default) =>
        await _db.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive || s.Id == includeInactiveId)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierOption
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                IsImporter = s.IsImporter,
                CurrencyCode = s.CurrencyCode,
                IsActive = s.IsActive,
            })
            .ToListAsync(cancellationToken);

    public async Task<OperationResult<long>> CreateAsync(
        SaveSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await NextCodeAsync(cancellationToken)
            : request.Code.Trim().ToUpperInvariant();

        var validation = await ValidateAsync(request, code, existingId: null, cancellationToken);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var supplier = new Supplier
        {
            Code = code,
            Name = request.Name.Trim(),
            ContactName = Trim(request.ContactName),
            Phone = Trim(request.Phone),
            Email = Trim(request.Email),
            AddressLine1 = Trim(request.AddressLine1),
            City = Trim(request.City),
            Country = Trim(request.Country),
            IsImporter = request.IsImporter,
            CurrencyCode = NormaliseCurrency(request),
            PaymentTermDays = request.PaymentTermDays,
            Bin = Trim(request.Bin),
            Notes = Trim(request.Notes),
            IsActive = request.IsActive,
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.SupplierCreated,
            nameof(Supplier),
            supplier.Id.ToString(CultureInfo.InvariantCulture),
            $"Created supplier {supplier.Code} - {supplier.Name}.",
            new { supplier.Code, supplier.Name, supplier.IsImporter, supplier.CurrencyCode },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(supplier.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (supplier is null)
        {
            return OperationResult.Failure("That supplier no longer exists.");
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? supplier.Code
            : request.Code.Trim().ToUpperInvariant();

        var validation = await ValidateAsync(request, code, id, cancellationToken);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var before = new
        {
            supplier.Code,
            supplier.Name,
            supplier.IsImporter,
            supplier.CurrencyCode,
            supplier.PaymentTermDays,
            supplier.IsActive,
        };

        supplier.Code = code;
        supplier.Name = request.Name.Trim();
        supplier.ContactName = Trim(request.ContactName);
        supplier.Phone = Trim(request.Phone);
        supplier.Email = Trim(request.Email);
        supplier.AddressLine1 = Trim(request.AddressLine1);
        supplier.City = Trim(request.City);
        supplier.Country = Trim(request.Country);
        supplier.IsImporter = request.IsImporter;
        supplier.CurrencyCode = NormaliseCurrency(request);
        supplier.PaymentTermDays = request.PaymentTermDays;
        supplier.Bin = Trim(request.Bin);
        supplier.Notes = Trim(request.Notes);
        supplier.IsActive = request.IsActive;

        await _audit.LogAsync(
            AuditActions.SupplierUpdated,
            nameof(Supplier),
            supplier.Id.ToString(CultureInfo.InvariantCulture),
            $"Updated supplier {supplier.Code} - {supplier.Name}.",
            new
            {
                Before = before,
                After = new
                {
                    supplier.Code,
                    supplier.Name,
                    supplier.IsImporter,
                    supplier.CurrencyCode,
                    supplier.PaymentTermDays,
                    supplier.IsActive,
                },
            },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Soft delete, refused once the supplier has delivered anything.
    ///
    /// A batch records where stock came from. Removing the supplier behind it
    /// would leave a recall with nobody to call.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (supplier is null)
        {
            return OperationResult.Failure("That supplier no longer exists.");
        }

        var batchCount = await _db.StockBatches.CountAsync(b => b.SupplierId == id, cancellationToken);

        if (batchCount > 0)
        {
            return OperationResult.Failure(
                $"{supplier.Name} has delivered {batchCount} batch(es) of stock, so they are part of its "
                + "history. Deactivate them instead - that hides them from the purchase screens without "
                + "breaking the trail back from a batch.");
        }

        supplier.IsDeleted = true;
        supplier.IsActive = false;

        await _audit.LogAsync(
            AuditActions.SupplierUpdated,
            nameof(Supplier),
            supplier.Id.ToString(CultureInfo.InvariantCulture),
            $"Deleted supplier {supplier.Code} - {supplier.Name}.",
            new { supplier.Code, supplier.Name, Deleted = true },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private async Task<OperationResult> ValidateAsync(
        SaveSupplierRequest request,
        string code,
        long? existingId,
        CancellationToken cancellationToken)
    {
        if (!CodePattern().IsMatch(code))
        {
            return OperationResult.Failure(
                "Use 2 to 20 characters: letters, numbers and dashes.",
                nameof(SaveSupplierRequest.Code));
        }

        if (await _db.Suppliers.AnyAsync(
                s => s.Code == code && (existingId == null || s.Id != existingId), cancellationToken))
        {
            return OperationResult.Failure(
                $"Supplier code '{code}' is already in use.", nameof(SaveSupplierRequest.Code));
        }

        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Failure("Enter a supplier name.", nameof(SaveSupplierRequest.Name));
        }

        if (name.Length > 200)
        {
            return OperationResult.Failure("Supplier name is too long.", nameof(SaveSupplierRequest.Name));
        }

        if (await _db.Suppliers.AnyAsync(
                s => s.Name == name && (existingId == null || s.Id != existingId), cancellationToken))
        {
            return OperationResult.Failure(
                $"'{name}' is already a supplier.", nameof(SaveSupplierRequest.Name));
        }

        if (request.PaymentTermDays is < 0 or > 365)
        {
            return OperationResult.Failure(
                "Payment terms must be between 0 and 365 days. Zero means paid on purchase.",
                nameof(SaveSupplierRequest.PaymentTermDays));
        }

        var currency = NormaliseCurrency(request);

        if (!CurrencyPattern().IsMatch(currency))
        {
            return OperationResult.Failure(
                "Use a three-letter currency code, for example BDT or USD.",
                nameof(SaveSupplierRequest.CurrencyCode));
        }

        // A local supplier invoicing in a foreign currency is almost always a
        // mistyped flag rather than a real arrangement, and it would put the
        // landed-cost fields somewhere nobody expects them.
        if (!request.IsImporter && currency != "BDT")
        {
            return OperationResult.Failure(
                "A local supplier invoices in BDT. Mark them as an import source if they bill in "
                + $"{currency}.",
                nameof(SaveSupplierRequest.CurrencyCode));
        }

        return OperationResult.Success();
    }

    private static string NormaliseCurrency(SaveSupplierRequest request) =>
        string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? "BDT"
            : request.CurrencyCode.Trim().ToUpperInvariant();

    /// <summary>
    /// Next code in the SUP-001 series. Gaps are harmless - codes are only
    /// generated when the field is left blank.
    /// </summary>
    private async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Suppliers
            .IgnoreQueryFilters()
            .Where(s => s.Code.StartsWith("SUP-"))
            .Select(s => s.Code)
            .ToListAsync(cancellationToken);

        var highest = existing
            .Select(c => c.Length == 7
                         && int.TryParse(c[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? n
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"SUP-{highest + 1:D3}";
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
