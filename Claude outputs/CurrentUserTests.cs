using System.Security.Claims;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Web.Security;
using Microsoft.AspNetCore.Http;

namespace EchoLifestyle.IntegrationTests.Security;

/// <summary>
/// Branch scoping and the staff/customer split as seen by application code.
/// </summary>
public class CurrentUserTests
{
    private static CurrentUser For(
        UserType? userType,
        long[]? branchIds = null,
        string[]? roles = null,
        string[]? permissions = null,
        bool authenticated = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "42") };

        if (userType is not null)
        {
            claims.Add(new Claim(EchoClaimTypes.UserType, userType.Value.ToString()));
        }

        foreach (var branchId in branchIds ?? [])
        {
            claims.Add(new Claim(EchoClaimTypes.Branch, branchId.ToString()));
        }

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in permissions ?? [])
        {
            claims.Add(new Claim(Permissions.ClaimType, permission));
        }

        var identity = authenticated
            ? new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role)
            : new ClaimsIdentity(claims);

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void Reads_the_user_id_from_the_principal()
    {
        Assert.Equal(42, For(UserType.Staff).UserId);
    }

    [Fact]
    public void An_anonymous_request_has_no_user_and_no_branches()
    {
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.False(user.IsAuthenticated);
        Assert.False(user.IsStaff);
        Assert.False(user.IsOwner);
        Assert.Null(user.UserId);
        Assert.Empty(user.BranchIds);
    }

    [Fact]
    public void Staff_are_scoped_to_the_branches_assigned_to_them()
    {
        var user = For(UserType.Staff, branchIds: [1, 3]);

        Assert.True(user.CanAccessBranch(1));
        Assert.True(user.CanAccessBranch(3));

        // The branch they were not assigned. This is the check that stops one
        // store manager reading another store's numbers.
        Assert.False(user.CanAccessBranch(2));
    }

    [Fact]
    public void An_owner_can_access_every_branch_including_unassigned_ones()
    {
        var user = For(UserType.Staff, branchIds: [1], roles: [Roles.Owner]);

        Assert.True(user.IsOwner);
        Assert.True(user.CanAccessBranch(99));
    }

    [Fact]
    public void A_customer_can_access_no_branch_even_with_branch_claims()
    {
        var user = For(UserType.Customer, branchIds: [1, 2], roles: [Roles.Owner]);

        Assert.False(user.IsStaff);
        Assert.False(user.IsOwner);
        Assert.False(user.CanAccessBranch(1));
    }

    [Fact]
    public void HasPermission_mirrors_the_authorization_handler()
    {
        // Menu rendering uses this. If it disagreed with the handler, people
        // would see links that error, or miss links they are entitled to.
        var staff = For(UserType.Staff, permissions: [Permissions.Sales.OrderView]);
        Assert.True(staff.HasPermission(Permissions.Sales.OrderView));
        Assert.False(staff.HasPermission(Permissions.Finance.JournalPost));

        var owner = For(UserType.Staff, roles: [Roles.Owner]);
        Assert.True(owner.HasPermission(Permissions.Finance.JournalPost));

        var customer = For(UserType.Customer, permissions: [Permissions.Sales.OrderView]);
        Assert.False(customer.HasPermission(Permissions.Sales.OrderView));
    }

    [Fact]
    public void Malformed_branch_claims_are_ignored_rather_than_crashing_the_request()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new(EchoClaimTypes.UserType, nameof(UserType.Staff)),
            new(EchoClaimTypes.Branch, "7"),
            new(EchoClaimTypes.Branch, "not-a-number"),
        };

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role)),
        };

        var user = new CurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.Equal([7L], user.BranchIds);
        Assert.False(user.CanAccessBranch(0));
    }
}
