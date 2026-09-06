using System.Text.RegularExpressions;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Administration.Warehouses;

/// <summary>
/// Warehouse administration.
///
/// Warehouses are not owned by a branch - several branches may draw from one,
/// and one branch may draw from several - so the links are edited on the branch
/// form and shown read-only here.
/// </summary>
public partial class WarehouseAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public WarehouseAdminService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]{1,19}$")]
    private static partial Regex CodePattern();

    public async Task<PagedResult<WarehouseListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Warehouses.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(w => w.IsActive),
            StatusFilter.Inactive => query.Where(w => !w.IsActive),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(w =>
                EF.Functions.Like(w.Code, $"%{term}%")
                || EF.Functions.Like(w.Name, $"%{term}%")
                || (w.City != null && EF.Functions.Like(w.City, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("name", false) => query.OrderBy(w => w.Name),
            ("name", true) => query.OrderByDescending(w => w.Name),
            ("kind", false) => query.OrderBy(w => w.Kind).ThenBy(w => w.Code),
            ("kind", true) => query.OrderByDescending(w => w.Kind).ThenBy(w => w.Code),
            ("city", false) => query.OrderBy(w => w.City).ThenBy(w => w.Code),
            ("city", true) => query.OrderByDescending(w => w.City).ThenBy(w => w.Code),
            ("isActive", false) => query.OrderBy(w => w.IsActive).ThenBy(w => w.Code),
            ("isActive", true) => query.OrderByDescending(w => w.IsActive).ThenBy(w => w.Code),
            ("code", true) => query.OrderByDescending(w => w.Code),
            _ => query.OrderBy(w => w.Code),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(w => new WarehouseListItem
            {
                Id = w.Id,
                Code = w.Code,
                Name = w.Name,
                Kind = w.Kind,
                City = w.City,
                IsActive = w.IsActive,
                BranchCount = w.BranchWarehouses.Count,
                PrimaryForBranchCount = w.BranchWarehouses.Count(bw => bw.IsPrimary),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<WarehouseListItem>(rows, totalCount, filteredCount);
    }

    public async Task<WarehouseDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _db.Warehouses
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new
            {
                w.Id,
                w.Code,
                w.Name,
                w.Kind,
                w.AddressLine1,
                w.City,
                w.IsActive,
                Links = w.BranchWarehouses
                    .Select(bw => new { bw.IsPrimary, BranchName = bw.Branch!.Code + " - " + bw.Branch.Name })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouse is null)
        {
            return null;
        }

        return new WarehouseDetail
        {
            Id = warehouse.Id,
            Code = warehouse.Code,
            Name = warehouse.Name,
            Kind = warehouse.Kind,
            AddressLine1 = warehouse.AddressLine1,
            City = warehouse.City,
            IsActive = warehouse.IsActive,
            LinkedBranches = warehouse.Links.Select(l => l.BranchName).OrderBy(n => n).ToList(),
            PrimaryForBranches = warehouse.Links
                .Where(l => l.IsPrimary)
                .Select(l => l.BranchName)
                .OrderBy(n => n)
                .ToList(),
        };
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var company = await _db.Companies.OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return OperationResult<long>.Failure("No company exists to attach this warehouse to.");
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await CodeExistsAsync(company.Id, code, null, cancellationToken))
        {
            return OperationResult<long>.Failure(
                $"Warehouse code '{code}' is already in use.", nameof(SaveWarehouseRequest.Code));
        }

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            Code = code,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            AddressLine1 = Trim(request.AddressLine1),
            City = Trim(request.City),
            IsActive = request.IsActive,
        };

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.WarehouseCreated,
            nameof(Warehouse),
            warehouse.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Created warehouse {warehouse.Code} - {warehouse.Name}.",
            new { warehouse.Code, warehouse.Name, warehouse.Kind },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(warehouse.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (warehouse is null)
        {
            return OperationResult.Failure("That warehouse no longer exists.");
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await CodeExistsAsync(warehouse.CompanyId, code, warehouse.Id, cancellationToken))
        {
            return OperationResult.Failure(
                $"Warehouse code '{code}' is already in use.", nameof(SaveWarehouseRequest.Code));
        }

        // Deactivating a warehouse that an active branch depends on as its
        // default source would leave that branch unable to fulfil anything.
        // Unlink it on the branch first, so the consequence is deliberate.
        if (warehouse.IsActive && !request.IsActive)
        {
            var blockingBranches = await _db.BranchWarehouses
                .Where(bw => bw.WarehouseId == warehouse.Id && bw.IsPrimary && bw.Branch!.IsActive)
                .Select(bw => bw.Branch!.Code)
                .ToListAsync(cancellationToken);

            if (blockingBranches.Count > 0)
            {
                return OperationResult.Failure(
                    $"This is the primary warehouse for {string.Join(", ", blockingBranches)}. "
                    + "Point those branches at another warehouse before deactivating it.",
                    nameof(SaveWarehouseRequest.IsActive));
            }
        }

        var before = new { warehouse.Code, warehouse.Name, warehouse.Kind, warehouse.IsActive };
        var kindChanged = warehouse.Kind != request.Kind;

        warehouse.Code = code;
        warehouse.Name = request.Name.Trim();
        warehouse.Kind = request.Kind;
        warehouse.AddressLine1 = Trim(request.AddressLine1);
        warehouse.City = Trim(request.City);
        warehouse.IsActive = request.IsActive;

        await _audit.LogAsync(
            AuditActions.WarehouseUpdated,
            nameof(Warehouse),
            warehouse.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Updated warehouse {warehouse.Code} - {warehouse.Name}.",
            new { Before = before, After = new { warehouse.Code, warehouse.Name, warehouse.Kind, warehouse.IsActive } },
            cancellationToken: cancellationToken);

        if (kindChanged)
        {
            // Moving stock in or out of "sellable" changes what the business can
            // sell without a single stock movement being recorded, so it gets
            // its own entry rather than hiding inside a general edit.
            await _audit.LogAsync(
                "Administration.Warehouse.KindChanged",
                nameof(Warehouse),
                warehouse.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Warehouse {warehouse.Code} changed from {before.Kind} to {warehouse.Kind}.",
                new { warehouse.Code, From = before.Kind, To = warehouse.Kind },
                cancellationToken: cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private static OperationResult Validate(SaveWarehouseRequest request)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            return OperationResult.Failure("Enter a warehouse code.", nameof(SaveWarehouseRequest.Code));
        }

        if (!CodePattern().IsMatch(code))
        {
            return OperationResult.Failure(
                "Use 2 to 20 characters: letters, numbers and dashes, starting with a letter or number.",
                nameof(SaveWarehouseRequest.Code));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult.Failure("Enter a warehouse name.", nameof(SaveWarehouseRequest.Name));
        }

        return OperationResult.Success();
    }

    private Task<bool> CodeExistsAsync(
        long companyId,
        string code,
        long? excludingId,
        CancellationToken cancellationToken) =>
        _db.Warehouses.AnyAsync(
            w => w.CompanyId == companyId
                 && w.Code == code
                 && (excludingId == null || w.Id != excludingId),
            cancellationToken);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
