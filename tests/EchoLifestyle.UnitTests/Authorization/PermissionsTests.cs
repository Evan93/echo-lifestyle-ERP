using EchoLifestyle.Application.Common.Authorization;

namespace EchoLifestyle.UnitTests.Authorization;

public class PermissionsTests
{
    [Fact]
    public void All_discovers_every_declared_permission()
    {
        Assert.NotEmpty(Permissions.All);

        // Spot-check one from each module so a whole module quietly vanishing
        // from discovery would fail here.
        Assert.Contains(Permissions.Security.RolePermissionsEdit, Permissions.All);
        Assert.Contains(Permissions.Inventory.TransferApprove, Permissions.All);
        Assert.Contains(Permissions.Finance.PeriodClose, Permissions.All);
        Assert.Contains(Permissions.Catalog.CostPriceView, Permissions.All);
    }

    [Fact]
    public void All_contains_no_duplicates()
    {
        // Two constants sharing a value would silently merge two different
        // capabilities into one grant.
        var duplicates = Permissions.All
            .GroupBy(p => p, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_permission_follows_the_Module_Entity_Action_shape()
    {
        // The policy provider and the sidebar both split on '.', and reporting
        // groups by module, so the shape is load-bearing rather than cosmetic.
        var malformed = Permissions.All
            .Where(p => p.Split('.').Length != 3 || p.Split('.').Any(string.IsNullOrWhiteSpace))
            .ToList();

        Assert.Empty(malformed);
    }

    [Fact]
    public void Exists_accepts_known_permissions_and_rejects_typos()
    {
        Assert.True(Permissions.Exists(Permissions.Sales.OrderCreate));

        // A mistyped policy name must fail closed. If Exists returned true for
        // unknown values, [Authorize(Policy = "Sales.Order.Craete")] would
        // build a policy nobody can satisfy - or worse, be ignored.
        Assert.False(Permissions.Exists("Sales.Order.Craete"));
        Assert.False(Permissions.Exists(string.Empty));
    }

    [Fact]
    public void ByModule_groups_permissions_under_their_module()
    {
        var byModule = Permissions.ByModule;

        Assert.Contains("Inventory", byModule.Keys);
        Assert.All(
            byModule,
            entry => Assert.All(entry.Value, p => Assert.StartsWith(entry.Key + ".", p, StringComparison.Ordinal)));
    }
}
