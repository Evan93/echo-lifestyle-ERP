using System.Security.Claims;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Web.Security;
using Microsoft.AspNetCore.Authorization;

namespace EchoLifestyle.IntegrationTests.Security;

/// <summary>
/// The rule that keeps shoppers out of the back office.
///
/// These need no database, but they live here because they exercise the web
/// layer. They are the most security-relevant tests in Phase 1.
/// </summary>
public class PermissionAuthorizationHandlerTests
{
    private const string Permission = Permissions.Catalog.CostPriceView;

    private static ClaimsPrincipal Principal(
        UserType? userType,
        string[]? permissions = null,
        string[]? roles = null,
        bool authenticated = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };

        if (userType is not null)
        {
            claims.Add(new Claim(EchoClaimTypes.UserType, userType.Value.ToString()));
        }

        foreach (var permission in permissions ?? [])
        {
            claims.Add(new Claim(Permissions.ClaimType, permission));
        }

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // An identity with no authentication type is treated as unauthenticated.
        var identity = authenticated
            ? new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role)
            : new ClaimsIdentity(claims);

        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> EvaluateAsync(ClaimsPrincipal user, string permission = Permission)
    {
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Staff_holding_the_permission_is_allowed()
    {
        var user = Principal(UserType.Staff, permissions: [Permission]);
        Assert.True(await EvaluateAsync(user));
    }

    [Fact]
    public async Task Staff_without_the_permission_is_refused()
    {
        var user = Principal(UserType.Staff, permissions: [Permissions.Sales.OrderView]);
        Assert.False(await EvaluateAsync(user));
    }

    [Fact]
    public async Task An_owner_is_allowed_without_holding_the_claim()
    {
        var user = Principal(UserType.Staff, roles: [Roles.Owner]);
        Assert.True(await EvaluateAsync(user));
    }

    [Fact]
    public async Task A_customer_account_is_refused_even_when_it_holds_the_permission()
    {
        // The point of the whole design: a mis-assigned role or a bad seed is
        // not enough to get a shopper into the back office.
        var user = Principal(UserType.Customer, permissions: [Permission]);
        Assert.False(await EvaluateAsync(user));
    }

    [Fact]
    public async Task A_customer_account_is_refused_even_when_it_holds_the_owner_role()
    {
        // The most dangerous misconfiguration imaginable, and it still fails.
        var user = Principal(UserType.Customer, permissions: [Permission], roles: [Roles.Owner]);
        Assert.False(await EvaluateAsync(user));
    }

    [Fact]
    public async Task An_account_with_no_user_type_claim_is_refused()
    {
        // Fail closed: a principal issued before the claim existed, or by some
        // future path that forgets it, must not be treated as staff.
        var user = Principal(userType: null, permissions: [Permission], roles: [Roles.Owner]);
        Assert.False(await EvaluateAsync(user));
    }

    [Fact]
    public async Task An_unauthenticated_principal_is_refused()
    {
        var user = Principal(UserType.Staff, permissions: [Permission], authenticated: false);
        Assert.False(await EvaluateAsync(user));
    }

    [Fact]
    public async Task Permission_matching_is_case_sensitive_and_exact()
    {
        var user = Principal(UserType.Staff, permissions: ["catalog.costprice.view"]);
        Assert.False(await EvaluateAsync(user));

        var prefixed = Principal(UserType.Staff, permissions: ["Catalog.CostPrice.ViewAll"]);
        Assert.False(await EvaluateAsync(prefixed));
    }
}
