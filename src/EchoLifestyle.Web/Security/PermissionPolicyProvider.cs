using EchoLifestyle.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EchoLifestyle.Web.Security;

/// <summary>
/// Builds an authorization policy on demand for any permission name, so
/// [Authorize(Policy = Permissions.Catalog.ProductEdit)] works without
/// registering ~70 policies by hand and without them drifting out of sync
/// with the permission list.
///
/// Unknown permission names are rejected rather than silently allowed - a
/// typo in a policy name must fail closed.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (Permissions.Exists(policyName))
        {
            var policy = new AuthorizationPolicyBuilder(AuthSchemes.BackOffice)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
