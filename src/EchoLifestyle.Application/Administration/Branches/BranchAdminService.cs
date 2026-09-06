using System.Text.RegularExpressions;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Administration.Branches;

/// <summary>
/// Branch administration.
///
/// A feature service rather than one class per operation: the brief calls for
/// use cases or feature services, and for a team of two the extra ceremony of
/// a class per verb buys nothing here. Business rules still live in this layer
/// rather than in the controller.
/// </summary>
public partial class BranchAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public BranchAdminService(IApplicationDbContext db, ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9\-]{1,19}$")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Branch list, paged and sorted in the database.
    ///
    /// Non-owners see only the branches assigned to them - the scope is applied
    /// to the query, not filtered out of the results afterwards.
    /// </summary>
    public async Task<PagedResult<BranchListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Branches.AsNoTracking();

        if (!_currentUser.IsOwner)
        {
            var branchIds = _currentUser.BranchIds;
            query = query.Where(b => branchIds.Contains(b.Id));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(b => b.IsActive),
            StatusFilter.Inactive => query.Where(b => !b.IsActive),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b =>
                EF.Functions.Like(b.Code, $"%{term}%")
                || EF.Functions.Like(b.Name, $"%{term}%")
                || (b.City != null && EF.Functions.Like(b.City, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        // Sort keys map through an explicit switch. A key that is not listed
        // falls back to the default order, so nothing from the client reaches
        // the query as an expression.
        query = (sortColumn, sortDescending) switch
        {
            ("name", false) => query.OrderBy(b => b.Name),
            ("name", true) => query.OrderByDescending(b => b.Name),
            ("type", false) => query.OrderBy(b => b.Type).ThenBy(b => b.Code),
            ("type", true) => query.OrderByDescending(b => b.Type).ThenBy(b => b.Code),
            ("city", false) => query.OrderBy(b => b.City).ThenBy(b => b.Code),
            ("city", true) => query.OrderByDescending(b => b.City).ThenBy(b => b.Code),
            ("isActive", false) => query.OrderBy(b => b.IsActive).ThenBy(b => b.Code),
            ("isActive", true) => query.OrderByDescending(b => b.IsActive).ThenBy(b => b.Code),
            ("code", true) => query.OrderByDescending(b => b.Code),
            _ => query.OrderBy(b => b.Code),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(b => new BranchListItem
            {
                Id = b.Id,
                Code = b.Code,
                Name = b.Name,
                Type = b.Type,
                City = b.City,
                IsActive = b.IsActive,
                AllowNegativeStock = b.AllowNegativeStock,
                WarehouseCount = b.BranchWarehouses.Count,
                PrimaryWarehouseName = b.BranchWarehouses
                    .Where(bw => bw.IsPrimary)
                    .Select(bw => bw.Warehouse!.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<BranchListItem>(rows, totalCount, filteredCount);
    }

    /// <summary>
    /// One branch plus every warehouse, flagged with whether it is linked.
    /// The form needs the full warehouse list to render its checkboxes.
    /// </summary>
    public async Task<BranchDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.CanAccessBranch(id))
        {
            return null;
        }

        var branch = await _db.Branches
            .AsNoTracking()
            .Include(b => b.BranchWarehouses)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (branch is null)
        {
            return null;
        }

        var warehouses = await _db.Warehouses
            .AsNoTracking()
            .Where(w => w.CompanyId == branch.CompanyId && w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => new { w.Id, w.Code, w.Name })
            .ToListAsync(cancellationToken);

        var links = branch.BranchWarehouses.ToDictionary(bw => bw.WarehouseId);

        return new BranchDetail
        {
            Id = branch.Id,
            Code = branch.Code,
            Name = branch.Name,
            Type = branch.Type,
            AddressLine1 = branch.AddressLine1,
            City = branch.City,
            Phone = branch.Phone,
            IsActive = branch.IsActive,
            AllowNegativeStock = branch.AllowNegativeStock,
            Warehouses = warehouses
                .Select(w => new BranchWarehouseLink
                {
                    WarehouseId = w.Id,
                    WarehouseCode = w.Code,
                    WarehouseName = w.Name,
                    IsLinked = links.ContainsKey(w.Id),
                    IsPrimary = links.TryGetValue(w.Id, out var link) && link.IsPrimary,
                    Priority = links.TryGetValue(w.Id, out var existing) ? existing.Priority : 100,
                })
                .ToList(),
        };
    }

    /// <summary>Every warehouse available to link, for a new branch's form.</summary>
    public async Task<IReadOnlyList<BranchWarehouseLink>> GetLinkableWarehousesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => new BranchWarehouseLink
            {
                WarehouseId = w.Id,
                WarehouseCode = w.Code,
                WarehouseName = w.Name,
                IsLinked = false,
                IsPrimary = false,
                Priority = 100,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var company = await _db.Companies
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            return OperationResult<long>.Failure("No company exists to attach this branch to.");
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await CodeExistsAsync(company.Id, code, excludingBranchId: null, cancellationToken))
        {
            return OperationResult<long>.Failure(
                $"Branch code '{code}' is already in use.", nameof(SaveBranchRequest.Code));
        }

        var branch = new Branch
        {
            CompanyId = company.Id,
            Code = code,
            Name = request.Name.Trim(),
            Type = request.Type,
            AddressLine1 = Trim(request.AddressLine1),
            City = Trim(request.City),
            Phone = Trim(request.Phone),
            IsActive = request.IsActive,
            AllowNegativeStock = request.AllowNegativeStock,
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(cancellationToken);

        var linkResult = await ApplyWarehousesAsync(branch, request.Warehouses, cancellationToken);
        if (!linkResult.Succeeded)
        {
            return OperationResult<long>.Failure(linkResult.Error!, linkResult.Field);
        }

        await _audit.LogAsync(
            AuditActions.BranchCreated,
            nameof(Branch),
            branch.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Created branch {branch.Code} - {branch.Name}.",
            new { branch.Code, branch.Name, branch.Type, branch.AllowNegativeStock },
            branch.Id,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(branch.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.CanAccessBranch(id))
        {
            return OperationResult.Failure("You do not have access to that branch.");
        }

        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var branch = await _db.Branches
            .Include(b => b.BranchWarehouses)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (branch is null)
        {
            return OperationResult.Failure("That branch no longer exists.");
        }

        var code = request.Code.Trim().ToUpperInvariant();

        if (await CodeExistsAsync(branch.CompanyId, code, branch.Id, cancellationToken))
        {
            return OperationResult.Failure(
                $"Branch code '{code}' is already in use.", nameof(SaveBranchRequest.Code));
        }

        // The business must always have somewhere to trade. Deactivating the
        // last active branch would leave orders, stock movements and documents
        // with nowhere to attach - so it is refused rather than merely warned
        // about. The same guard belongs on warehouses and on the last Owner
        // account: the system should not let anyone configure it into being
        // unable to operate.
        if (branch.IsActive && !request.IsActive)
        {
            var otherActive = await _db.Branches.CountAsync(
                b => b.CompanyId == branch.CompanyId && b.Id != branch.Id && b.IsActive,
                cancellationToken);

            if (otherActive == 0)
            {
                return OperationResult.Failure(
                    "This is the only active branch. Activate another branch before deactivating this one - "
                    + "with none active, no order or stock movement would have anywhere to go.",
                    nameof(SaveBranchRequest.IsActive));
            }
        }

        var before = new
        {
            branch.Code,
            branch.Name,
            branch.Type,
            branch.IsActive,
            branch.AllowNegativeStock,
        };

        branch.Code = code;
        branch.Name = request.Name.Trim();
        branch.Type = request.Type;
        branch.AddressLine1 = Trim(request.AddressLine1);
        branch.City = Trim(request.City);
        branch.Phone = Trim(request.Phone);
        branch.IsActive = request.IsActive;

        // Weakening the negative-stock guard is logged as its own action so it
        // is findable in the audit trail without reading every branch edit.
        var negativeStockChanged = branch.AllowNegativeStock != request.AllowNegativeStock;
        branch.AllowNegativeStock = request.AllowNegativeStock;

        var linkResult = await ApplyWarehousesAsync(branch, request.Warehouses, cancellationToken);
        if (!linkResult.Succeeded)
        {
            return linkResult;
        }

        await _audit.LogAsync(
            AuditActions.BranchUpdated,
            nameof(Branch),
            branch.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Updated branch {branch.Code} - {branch.Name}.",
            new { Before = before, After = new { branch.Code, branch.Name, branch.Type, branch.IsActive, branch.AllowNegativeStock } },
            branch.Id,
            cancellationToken);

        if (negativeStockChanged)
        {
            await _audit.LogAsync(
                "Administration.Branch.NegativeStockPolicyChanged",
                nameof(Branch),
                branch.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                branch.AllowNegativeStock
                    ? $"Negative stock ENABLED for branch {branch.Code}."
                    : $"Negative stock disabled for branch {branch.Code}.",
                new { branch.Code, branch.AllowNegativeStock },
                branch.Id,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Reconciles the branch's warehouse links with what the form submitted.
    ///
    /// The database also enforces one primary per branch through a filtered
    /// unique index; this check exists so the user gets a sensible message
    /// instead of a constraint violation.
    /// </summary>
    private async Task<OperationResult> ApplyWarehousesAsync(
        Branch branch,
        IReadOnlyCollection<WarehouseAssignment> assignments,
        CancellationToken cancellationToken)
    {
        var linked = assignments.Where(a => a.IsLinked).ToList();

        if (linked.Count > 0 && linked.Count(a => a.IsPrimary) != 1)
        {
            return OperationResult.Failure(
                "Choose exactly one primary warehouse - it is the default source when a document does not name one.");
        }

        var warehouseIds = linked.Select(a => a.WarehouseId).ToList();

        var valid = await _db.Warehouses
            .Where(w => w.CompanyId == branch.CompanyId && warehouseIds.Contains(w.Id))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        if (valid.Count != warehouseIds.Count)
        {
            return OperationResult.Failure("One of the selected warehouses no longer exists.");
        }

        var existing = await _db.BranchWarehouses
            .Where(bw => bw.BranchId == branch.Id)
            .ToListAsync(cancellationToken);

        foreach (var link in existing.Where(e => !warehouseIds.Contains(e.WarehouseId)))
        {
            _db.BranchWarehouses.Remove(link);
        }

        foreach (var assignment in linked)
        {
            var link = existing.FirstOrDefault(e => e.WarehouseId == assignment.WarehouseId);

            if (link is null)
            {
                _db.BranchWarehouses.Add(new BranchWarehouse
                {
                    BranchId = branch.Id,
                    WarehouseId = assignment.WarehouseId,
                    IsPrimary = assignment.IsPrimary,
                    Priority = assignment.Priority,
                });
            }
            else
            {
                link.IsPrimary = assignment.IsPrimary;
                link.Priority = assignment.Priority;
            }
        }

        return OperationResult.Success();
    }

    private static OperationResult Validate(SaveBranchRequest request)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            return OperationResult.Failure("Enter a branch code.", nameof(SaveBranchRequest.Code));
        }

        if (!CodePattern().IsMatch(code))
        {
            return OperationResult.Failure(
                "Use 2 to 20 characters: letters, numbers and dashes, starting with a letter or number.",
                nameof(SaveBranchRequest.Code));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult.Failure("Enter a branch name.", nameof(SaveBranchRequest.Name));
        }

        if (request.Name.Trim().Length > 200)
        {
            return OperationResult.Failure("Branch name is too long.", nameof(SaveBranchRequest.Name));
        }

        return OperationResult.Success();
    }

    private Task<bool> CodeExistsAsync(
        long companyId,
        string code,
        long? excludingBranchId,
        CancellationToken cancellationToken) =>
        _db.Branches.AnyAsync(
            b => b.CompanyId == companyId
                 && b.Code == code
                 && (excludingBranchId == null || b.Id != excludingBranchId),
            cancellationToken);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
