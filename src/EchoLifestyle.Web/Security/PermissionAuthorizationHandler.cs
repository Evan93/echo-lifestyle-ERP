using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Domain.Security;
using Microsoft.AspNetCore.Authorization;

namespace EchoLifestyle.Web.Security;

/// <summary>
/// Grants a permission only when BOTH of these hold:
///
///   1. the account is UserType.Staff, and
///   2. it holds the permission claim, or the Owner role.
///
/// The UserType test is the important one. Roles and claims are mutable data;
/// a mis-assigned role, a bad seed or a compromised shopper account would
/// otherwise be one row away from cost prices and partner capital. Requiring
/// Staff as well means such an account is still refused.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var userType = user.FindFirst(EchoClaimTypes.UserType)?.Value;
        if (!string.Equals(userType, nameof(UserType.Staff), StringComparison.Ordinal))
        {
            // A customer account never satisfies a back-office permission,
            // whatever roles or claims it happens to carry.
            return Task.CompletedTask;
        }

        // Owners hold every permission implicitly, so adding a new permission
        // to the system can never lock the business out of its own admin.
        if (user.IsInRole(Roles.Owner))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var hasPermission = user.HasClaim(
            c => string.Equals(c.Type, Permissions.ClaimType, StringComparison.Ordinal)
                 && string.Equals(c.Value, requirement.Permission, StringComparison.Ordinal));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
