using System.Security.Claims;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Infrastructure.Identity;

/// <summary>
/// Roles and their permissions.
///
/// Permissions are stored as role claims of type <c>permission</c>. Nothing in
/// the system authorises on a role name, so roles can be renamed and reshaped
/// freely - with two exceptions that would otherwise let someone lock the
/// business out: seeded roles cannot be deleted, and Owner always holds every
/// permission.
///
/// Lives in Infrastructure for the same reason as UserAdminService: it is
/// expressed entirely through ASP.NET Identity's RoleManager.
/// </summary>
public class RoleAdminService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EchoDbContext _db;
    private readonly IAuditLogger _audit;

    public RoleAdminService(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        EchoDbContext db,
        IAuditLogger audit)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Roles number in the low tens and are read as a whole, so this list is
        // deliberately not paged - a server-side grid for fourteen rows would be
        // machinery without a purpose.
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSystemRole,
                PermissionCount = _db.RoleClaims.Count(c => c.RoleId == r.Id && c.ClaimType == Permissions.ClaimType),
                UserCount = _db.UserRoles.Count(ur => ur.RoleId == r.Id),
            })
            .ToListAsync(cancellationToken);

        return roles
            .Select(r => new RoleListItem
            {
                Id = r.Id,
                Name = r.Name!,
                Description = r.Description,
                IsSystemRole = r.IsSystemRole,
                PermissionsAreFixed = r.Name == Roles.Owner,
                PermissionCount = r.Name == Roles.Owner ? Permissions.All.Count : r.PermissionCount,
                UserCount = r.UserCount,
            })
            .ToList();
    }

    public async Task<RoleDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (role is null)
        {
            return null;
        }

        var isOwner = role.Name == Roles.Owner;

        var permissions = isOwner
            ? Permissions.All
            : await _db.RoleClaims
                .Where(c => c.RoleId == role.Id && c.ClaimType == Permissions.ClaimType)
                .Select(c => c.ClaimValue!)
                .ToListAsync(cancellationToken);

        return new RoleDetail
        {
            Id = role.Id,
            Name = role.Name!,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            PermissionsAreFixed = isOwner,
            UserCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id, cancellationToken),
            Permissions = permissions.ToHashSet(StringComparer.Ordinal),
        };
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var name = request.Name.Trim();

        if (await _roleManager.RoleExistsAsync(name))
        {
            return OperationResult<long>.Failure(
                $"A role named '{name}' already exists.", nameof(SaveRoleRequest.Name));
        }

        var role = new ApplicationRole
        {
            Name = name,
            Description = Trim(request.Description),
            IsSystemRole = false,
        };

        var created = await _roleManager.CreateAsync(role);
        if (!created.Succeeded)
        {
            return OperationResult<long>.Failure(Describe(created), nameof(SaveRoleRequest.Name));
        }

        await SetPermissionsAsync(role, request.Permissions, cancellationToken);

        await _audit.LogAsync(
            AuditActions.RoleCreated,
            nameof(ApplicationRole),
            role.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Created role '{role.Name}' with {request.Permissions.Count} permissions.",
            new { role.Name, Permissions = request.Permissions },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(role.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (role is null)
        {
            return OperationResult.Failure("That role no longer exists.");
        }

        // Owner always holds everything. Editing its permissions would only
        // create a way to strip the one role that can put them back.
        if (role.Name == Roles.Owner)
        {
            return OperationResult.Failure(
                "Owner permissions are fixed - the role always holds every permission, including any added later.");
        }

        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var name = request.Name.Trim();

        // Seeded roles keep their names: the seeder looks them up by name, and a
        // rename would make it recreate the role on next startup.
        if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            return OperationResult.Failure(
                "System roles cannot be renamed. Their permissions can still be changed.",
                nameof(SaveRoleRequest.Name));
        }

        if (!role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            if (await _roleManager.RoleExistsAsync(name))
            {
                return OperationResult.Failure(
                    $"A role named '{name}' already exists.", nameof(SaveRoleRequest.Name));
            }

            role.Name = name;
        }

        role.Description = Trim(request.Description);

        var updated = await _roleManager.UpdateAsync(role);
        if (!updated.Succeeded)
        {
            return OperationResult.Failure(Describe(updated));
        }

        var before = await _db.RoleClaims
            .Where(c => c.RoleId == role.Id && c.ClaimType == Permissions.ClaimType)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken);

        var changed = await SetPermissionsAsync(role, request.Permissions, cancellationToken);

        await _audit.LogAsync(
            AuditActions.RolePermissionsChanged,
            nameof(ApplicationRole),
            role.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Updated role '{role.Name}'.",
            new
            {
                role.Name,
                Granted = request.Permissions.Except(before, StringComparer.Ordinal).ToList(),
                Revoked = before.Except(request.Permissions, StringComparer.Ordinal).ToList(),
            },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            await RefreshMembersAsync(role.Id, cancellationToken);
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (role is null)
        {
            return OperationResult.Failure("That role no longer exists.");
        }

        if (role.IsSystemRole)
        {
            return OperationResult.Failure(
                $"'{role.Name}' is a system role and cannot be deleted. Remove its permissions instead if it is unused.");
        }

        var userCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id, cancellationToken);

        if (userCount > 0)
        {
            return OperationResult.Failure(
                $"{userCount} account(s) still hold this role. Remove it from them first.");
        }

        var deleted = await _roleManager.DeleteAsync(role);
        if (!deleted.Succeeded)
        {
            return OperationResult.Failure(Describe(deleted));
        }

        await _audit.LogAsync(
            AuditActions.RoleDeleted,
            nameof(ApplicationRole),
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Deleted role '{role.Name}'.",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>Adds and removes permission claims. Returns true if anything changed.</summary>
    private async Task<bool> SetPermissionsAsync(
        ApplicationRole role,
        IReadOnlyCollection<string> requested,
        CancellationToken cancellationToken)
    {
        // Unknown values are dropped rather than stored: a claim nobody checks
        // is worse than no claim, because it looks like a granted capability.
        var desired = requested.Where(Permissions.Exists).ToHashSet(StringComparer.Ordinal);

        var existing = (await _roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == Permissions.ClaimType)
            .ToList();

        var existingValues = existing.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        var toAdd = desired.Except(existingValues, StringComparer.Ordinal).ToList();
        var toRemove = existing.Where(c => !desired.Contains(c.Value)).ToList();

        foreach (var permission in toAdd)
        {
            await _roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
        }

        foreach (var claim in toRemove)
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return toAdd.Count > 0 || toRemove.Count > 0;
    }

    /// <summary>
    /// Forces everyone holding this role to pick up the change.
    ///
    /// Permissions live in the sign-in cookie, so without this a revoked
    /// permission would keep working until the person happened to sign out -
    /// which is exactly the wrong direction for a security control to fail in.
    /// Bumping the security stamp makes the cookie stale, and the validator
    /// rebuilds it from the database on their next request.
    /// </summary>
    private async Task RefreshMembersAsync(long roleId, CancellationToken cancellationToken)
    {
        var userIds = await _db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            var user = await _userManager.FindByIdAsync(
                userId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (user is not null)
            {
                await _userManager.UpdateSecurityStampAsync(user);
            }
        }
    }

    private static OperationResult Validate(SaveRoleRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Failure("Enter a role name.", nameof(SaveRoleRequest.Name));
        }

        if (name.Length > 100)
        {
            return OperationResult.Failure("That role name is too long.", nameof(SaveRoleRequest.Name));
        }

        return OperationResult.Success();
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
