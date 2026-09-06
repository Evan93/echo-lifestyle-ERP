using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Security;

namespace EchoLifestyle.Web.Security;

/// <summary>
/// Resolves the acting user from the current request. Application code depends
/// on ICurrentUser rather than on HttpContext, so use cases stay testable.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public long? UserId
    {
        get
        {
            var value = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(value, CultureInfo.InvariantCulture, out var id) ? id : null;
        }
    }

    public string? UserName => Principal?.Identity?.Name;

    public UserType? UserType
    {
        get
        {
            var value = Principal?.FindFirst(EchoClaimTypes.UserType)?.Value;
            return Enum.TryParse<UserType>(value, ignoreCase: false, out var parsed) ? parsed : null;
        }
    }

    public bool IsStaff => IsAuthenticated && UserType == Domain.Security.UserType.Staff;

    public bool IsOwner => IsStaff && Principal?.IsInRole(Roles.Owner) == true;

    public IReadOnlyCollection<long> BranchIds =>
        Principal?.FindAll(EchoClaimTypes.Branch)
            .Select(c => long.TryParse(c.Value, CultureInfo.InvariantCulture, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray()
        ?? [];

    public bool HasPermission(string permission)
    {
        // Mirrors PermissionAuthorizationHandler exactly. Used for rendering
        // decisions only - it is never the enforcement point.
        if (!IsStaff)
        {
            return false;
        }

        if (IsOwner)
        {
            return true;
        }

        return Principal?.HasClaim(
            c => string.Equals(c.Type, Permissions.ClaimType, StringComparison.Ordinal)
                 && string.Equals(c.Value, permission, StringComparison.Ordinal)) == true;
    }

    public bool CanAccessBranch(long branchId)
    {
        if (!IsStaff)
        {
            return false;
        }

        return IsOwner || BranchIds.Contains(branchId);
    }

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? TraceId =>
        Activity.Current?.Id ?? _accessor.HttpContext?.TraceIdentifier;
}
