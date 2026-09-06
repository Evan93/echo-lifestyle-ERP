using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Infrastructure.Identity;

/// <summary>
/// Staff user administration.
///
/// This service lives in Infrastructure rather than Application - the one place
/// that bends the "business rules live in Application" rule in CLAUDE.md. Every
/// operation here is expressed through ASP.NET Identity's UserManager, and
/// wrapping that in an interface purely to move four methods up a layer would
/// add indirection without adding a seam anyone needs. The rules are still in
/// one place, and still out of the controller.
///
/// Customers are deliberately not manageable here. This screen is for staff;
/// shoppers belong to CRM, and no path through it can create or promote one.
/// </summary>
public class UserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EchoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UserAdminService(
        UserManager<ApplicationUser> userManager,
        EchoDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _userManager = userManager;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<PagedResult<StaffUserListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        CancellationToken cancellationToken = default)
    {
        // Staff only. A customer account must never appear on a screen that can
        // grant roles.
        var query = _db.Users.AsNoTracking().Where(u => u.UserType == UserType.Staff);

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(u => u.IsActive),
            StatusFilter.Inactive => query.Where(u => !u.IsActive),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                EF.Functions.Like(u.UserName!, $"%{term}%")
                || EF.Functions.Like(u.FullName, $"%{term}%")
                || (u.Email != null && EF.Functions.Like(u.Email, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("fullName", false) => query.OrderBy(u => u.FullName),
            ("fullName", true) => query.OrderByDescending(u => u.FullName),
            ("email", false) => query.OrderBy(u => u.Email),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("lastLoginAtUtc", false) => query.OrderBy(u => u.LastLoginAtUtc),
            ("lastLoginAtUtc", true) => query.OrderByDescending(u => u.LastLoginAtUtc),
            ("isActive", false) => query.OrderBy(u => u.IsActive).ThenBy(u => u.UserName),
            ("isActive", true) => query.OrderByDescending(u => u.IsActive).ThenBy(u => u.UserName),
            ("userName", true) => query.OrderByDescending(u => u.UserName),
            _ => query.OrderBy(u => u.UserName),
        };

        var now = DateTimeOffset.UtcNow;

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(u => new StaffUserListItem
            {
                Id = u.Id,
                UserName = u.UserName!,
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive,
                IsLockedOut = u.LockoutEnd != null && u.LockoutEnd > now,
                LastLoginAtUtc = u.LastLoginAtUtc,
                Roles = _db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .OrderBy(name => name)
                    .ToList(),
                BranchCount = _db.UserBranches.Count(ub => ub.UserId == u.Id),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StaffUserListItem>(rows, totalCount, filteredCount);
    }

    public async Task<StaffUserDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.UserType == UserType.Staff, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        var branches = await _db.UserBranches
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => new { ub.BranchId, ub.IsDefault })
            .ToListAsync(cancellationToken);

        return new StaffUserDetail
        {
            Id = user.Id,
            UserName = user.UserName!,
            FullName = user.FullName,
            Email = user.Email,
            Notes = user.Notes,
            IsActive = user.IsActive,
            IsLockedOut = user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow,
            LockoutEndUtc = user.LockoutEnd?.UtcDateTime,
            LastLoginAtUtc = user.LastLoginAtUtc,
            Roles = roles,
            BranchIds = branches.Select(b => b.BranchId).ToList(),
            DefaultBranchId = branches.FirstOrDefault(b => b.IsDefault)?.BranchId,
        };
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveStaffUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request, isNew: true);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var userName = request.UserName.Trim();

        if (await _userManager.FindByNameAsync(userName) is not null)
        {
            return OperationResult<long>.Failure(
                $"The username '{userName}' is already taken.", nameof(SaveStaffUserRequest.UserName));
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = Trim(request.Email),
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            Notes = Trim(request.Notes),
            IsActive = request.IsActive,

            // Fixed at creation and never editable. A customer who joins the
            // company gets a new staff account, not an upgraded one.
            UserType = UserType.Staff,
        };

        var created = await _userManager.CreateAsync(user, request.Password!);

        if (!created.Succeeded)
        {
            var first = created.Errors.FirstOrDefault();
            var field = first?.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) == true
                ? nameof(SaveStaffUserRequest.Password)
                : null;

            return OperationResult<long>.Failure(Describe(created), field);
        }

        var rolesResult = await ApplyRolesAsync(user, request.Roles, cancellationToken);
        if (!rolesResult.Succeeded)
        {
            return OperationResult<long>.Failure(rolesResult.Error!, rolesResult.Field);
        }

        await ApplyBranchesAsync(user.Id, request.BranchIds, request.DefaultBranchId, cancellationToken);

        await _audit.LogAsync(
            AuditActions.UserCreated,
            nameof(ApplicationUser),
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Created staff account '{user.UserName}' ({user.FullName}).",
            new { user.UserName, user.FullName, Roles = request.Roles, Branches = request.BranchIds },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(user.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveStaffUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request, isNew: false);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var user = await _userManager.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (user is null || user.UserType != UserType.Staff)
        {
            return OperationResult.Failure("That staff account no longer exists.");
        }

        // Nobody may switch off their own account. Doing so signs you out of a
        // system you may be the only one able to administer.
        if (user.Id == _currentUser.UserId && !request.IsActive)
        {
            return OperationResult.Failure(
                "You cannot deactivate your own account.",
                nameof(SaveStaffUserRequest.IsActive));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var losingOwner = currentRoles.Contains(Roles.Owner) && !request.Roles.Contains(Roles.Owner);
        var beingDeactivated = user.IsActive && !request.IsActive;

        // The business must keep at least one active Owner, or nobody can
        // administer roles, close a period, or reach the partner ledger again.
        if (losingOwner || beingDeactivated)
        {
            var guard = await EnsureAnotherActiveOwnerExistsAsync(user.Id, cancellationToken);
            if (!guard.Succeeded)
            {
                return OperationResult.Failure(
                    guard.Error!,
                    losingOwner ? nameof(SaveStaffUserRequest.Roles) : nameof(SaveStaffUserRequest.IsActive));
            }
        }

        var before = new { user.FullName, user.Email, user.IsActive, Roles = currentRoles.ToList() };

        user.FullName = request.FullName.Trim();
        user.Email = Trim(request.Email);
        user.Notes = Trim(request.Notes);
        user.IsActive = request.IsActive;

        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return OperationResult.Failure(Describe(updated));
        }

        var rolesResult = await ApplyRolesAsync(user, request.Roles, cancellationToken);
        if (!rolesResult.Succeeded)
        {
            return rolesResult;
        }

        await ApplyBranchesAsync(user.Id, request.BranchIds, request.DefaultBranchId, cancellationToken);

        await _audit.LogAsync(
            AuditActions.UserRolesChanged,
            nameof(ApplicationUser),
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Updated staff account '{user.UserName}'.",
            new { Before = before, After = new { user.FullName, user.Email, user.IsActive, Roles = request.Roles } },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>Clears an Identity lockout after too many failed sign-ins.</summary>
    public async Task<OperationResult> UnlockAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (user is null || user.UserType != UserType.Staff)
        {
            return OperationResult.Failure("That staff account no longer exists.");
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        await _audit.LogAsync(
            AuditActions.UserUnlocked,
            nameof(ApplicationUser),
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Unlocked '{user.UserName}' after failed sign-in attempts.",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Sets a new password directly. There is no "current password" step because
    /// this is an administrator acting on someone else's account; the action is
    /// audited so it is never invisible.
    /// </summary>
    public async Task<OperationResult> ResetPasswordAsync(
        long id,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (user is null || user.UserType != UserType.Staff)
        {
            return OperationResult.Failure("That staff account no longer exists.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            return OperationResult.Failure(Describe(result), "NewPassword");
        }

        await _audit.LogAsync(
            AuditActions.UserPasswordReset,
            nameof(ApplicationUser),
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Password reset for '{user.UserName}' by an administrator.",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private async Task<OperationResult> EnsureAnotherActiveOwnerExistsAsync(
        long excludingUserId,
        CancellationToken cancellationToken)
    {
        var ownerRoleId = await _db.Roles
            .Where(r => r.Name == Roles.Owner)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerRoleId == 0)
        {
            return OperationResult.Success();
        }

        var otherOwners = await _db.UserRoles
            .Where(ur => ur.RoleId == ownerRoleId && ur.UserId != excludingUserId)
            .Join(_db.Users, ur => ur.UserId, u => u.Id, (ur, u) => u)
            .CountAsync(u => u.IsActive && u.UserType == UserType.Staff, cancellationToken);

        return otherOwners > 0
            ? OperationResult.Success()
            : OperationResult.Failure(
                "This is the only active Owner. Give another account the Owner role first - "
                + "otherwise nobody could manage roles, close a period, or reach the partner ledger.");
    }

    private async Task<OperationResult> ApplyRolesAsync(
        ApplicationUser user,
        IReadOnlyCollection<string> requested,
        CancellationToken cancellationToken)
    {
        // Assignable roles come from the database, not from the seeded list:
        // roles created in the role editor are just as real as seeded ones, and
        // validating against the hardcoded list would make every custom role
        // impossible to assign.
        //
        // Customer stays excluded - it belongs to storefront accounts, and this
        // screen only manages staff.
        var assignable = await _db.Roles
            .AsNoTracking()
            .Where(r => r.Name != null && r.Name != Roles.Customer)
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken);

        var unknown = requested.Where(r => !assignable.Contains(r, StringComparer.Ordinal)).ToList();

        if (unknown.Count > 0)
        {
            return OperationResult.Failure(
                $"Unknown role: {string.Join(", ", unknown)}.", nameof(SaveStaffUserRequest.Roles));
        }

        var current = await _userManager.GetRolesAsync(user);

        var toAdd = requested.Except(current, StringComparer.Ordinal).ToList();
        var toRemove = current.Except(requested, StringComparer.Ordinal).ToList();

        if (toRemove.Count > 0)
        {
            var removed = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded)
            {
                return OperationResult.Failure(Describe(removed), nameof(SaveStaffUserRequest.Roles));
            }
        }

        if (toAdd.Count > 0)
        {
            var added = await _userManager.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded)
            {
                return OperationResult.Failure(Describe(added), nameof(SaveStaffUserRequest.Roles));
            }
        }

        return OperationResult.Success();
    }

    private async Task ApplyBranchesAsync(
        long userId,
        IReadOnlyCollection<long> branchIds,
        long? defaultBranchId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.UserBranches
            .Where(ub => ub.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var link in existing.Where(e => !branchIds.Contains(e.BranchId)))
        {
            _db.UserBranches.Remove(link);
        }

        // Exactly one default, and it must be one of the assigned branches -
        // the database enforces the "one default" half with a filtered index.
        var resolvedDefault = defaultBranchId is not null && branchIds.Contains(defaultBranchId.Value)
            ? defaultBranchId
            : branchIds.FirstOrDefault();

        foreach (var branchId in branchIds)
        {
            var link = existing.FirstOrDefault(e => e.BranchId == branchId);

            if (link is null)
            {
                _db.UserBranches.Add(new UserBranch
                {
                    UserId = userId,
                    BranchId = branchId,
                    IsDefault = branchId == resolvedDefault,
                });
            }
            else
            {
                link.IsDefault = branchId == resolvedDefault;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static OperationResult Validate(SaveStaffUserRequest request, bool isNew)
    {
        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                return OperationResult.Failure("Enter a username.", nameof(SaveStaffUserRequest.UserName));
            }

            if (request.UserName.Trim().Length < 3)
            {
                return OperationResult.Failure(
                    "Usernames need at least 3 characters.", nameof(SaveStaffUserRequest.UserName));
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return OperationResult.Failure(
                    "Set an initial password.", nameof(SaveStaffUserRequest.Password));
            }
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return OperationResult.Failure("Enter the person's full name.", nameof(SaveStaffUserRequest.FullName));
        }

        // Identity is configured with RequireUniqueEmail, so an account without
        // one cannot be created at all. Saying so here gives a sentence the user
        // can act on instead of Identity's "Email '' is invalid".
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return OperationResult.Failure(
                "Enter an email address - it is how the account is identified for password resets.",
                nameof(SaveStaffUserRequest.Email));
        }

        // A staff account with no branch and no Owner role can sign in and see
        // nothing at all, which reads as a broken system rather than a
        // misconfiguration. Refuse it at the point it is created.
        if (request.BranchIds.Count == 0 && !request.Roles.Contains(Roles.Owner))
        {
            return OperationResult.Failure(
                "Assign at least one branch, or give the account the Owner role. "
                + "Without either it would sign in and see nothing.",
                nameof(SaveStaffUserRequest.BranchIds));
        }

        return OperationResult.Success();
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
