using EchoLifestyle.Application.Common.Authorization;

namespace EchoLifestyle.UnitTests.Authorization;

public class RolesTests
{
    [Fact]
    public void Default_grants_reference_only_permissions_that_exist()
    {
        // Catches a renamed or deleted permission that a role still refers to.
        // Without this, the seeder would grant a claim nothing can ever check.
        var unknown = Roles.DefaultPermissions
            .SelectMany(role => role.Value.Select(permission => new { Role = role.Key, Permission = permission }))
            .Where(x => !Permissions.Exists(x.Permission))
            .Select(x => $"{x.Role} -> {x.Permission}")
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Customer_role_holds_no_back_office_permissions()
    {
        // Storefront access comes from UserType, never from back-office grants.
        // If this ever fails, a shopper account is one role assignment away
        // from cost prices and partner capital.
        Assert.Empty(Roles.DefaultPermissions[Roles.Customer]);
    }

    [Fact]
    public void Owner_is_not_listed_in_default_grants()
    {
        // Owner is granted every permission by the seeder instead of a fixed
        // list, so adding a new permission cannot lock the owners out.
        Assert.False(Roles.DefaultPermissions.ContainsKey(Roles.Owner));
    }

    [Fact]
    public void Every_staff_role_except_Owner_has_default_grants_defined()
    {
        var missing = Roles.StaffRoles
            .Where(role => role != Roles.Owner && !Roles.DefaultPermissions.ContainsKey(role))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void All_includes_every_staff_role_plus_customer()
    {
        Assert.Equal(Roles.StaffRoles.Count + 1, Roles.All.Count);
        Assert.Contains(Roles.Customer, Roles.All);
        Assert.DoesNotContain(Roles.Customer, Roles.StaffRoles);
    }

    [Fact]
    public void Role_names_are_unique()
    {
        Assert.Equal(Roles.All.Count, Roles.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(Roles.Auditor, Permissions.Finance.JournalPost)]
    [InlineData(Roles.Salesperson, Permissions.Catalog.CostPriceView)]
    [InlineData(Roles.Salesperson, Permissions.Finance.JournalView)]
    [InlineData(Roles.WebsiteManager, Permissions.Finance.JournalView)]
    public void Sensitive_permissions_are_withheld_from_roles_that_should_not_have_them(
        string role,
        string permission)
    {
        // Named cases rather than a blanket rule: an auditor reads but never
        // posts, and a salesperson must not see what stock costs.
        Assert.DoesNotContain(permission, Roles.DefaultPermissions[role]);
    }
}
