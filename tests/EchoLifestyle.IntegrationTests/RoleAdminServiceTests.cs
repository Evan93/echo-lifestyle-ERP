using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(IdentityCollection.Name)]
public class RoleAdminServiceTests
{
    private readonly IdentityFixture _fixture;

    public RoleAdminServiceTests(IdentityFixture fixture)
    {
        _fixture = fixture;
    }

    private static SaveRoleRequest Request(string name, params string[] permissions) => new()
    {
        Name = name,
        Description = "Created by a test",
        Permissions = [.. permissions],
    };

    [Fact]
    public async Task Owner_permissions_cannot_be_edited()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var ownerId = await db.Roles.Where(r => r.Name == Roles.Owner).Select(r => r.Id).SingleAsync();

        // Stripping Owner would remove the only role capable of putting
        // permissions back, so the whole operation is refused.
        var result = await service.UpdateAsync(ownerId, Request(Roles.Owner, Permissions.Sales.OrderView));

        Assert.False(result.Succeeded);
        Assert.Contains("fixed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_reports_every_permission_that_exists()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var ownerId = await db.Roles.Where(r => r.Name == Roles.Owner).Select(r => r.Id).SingleAsync();
        var detail = await service.GetAsync(ownerId);

        Assert.NotNull(detail);
        Assert.True(detail!.PermissionsAreFixed);
        Assert.Equal(Permissions.All.Count, detail.Permissions.Count);
    }

    [Fact]
    public async Task A_system_role_cannot_be_renamed_but_its_permissions_can_change()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var accountantId = await db.Roles
            .Where(r => r.Name == Roles.Accountant)
            .Select(r => r.Id)
            .SingleAsync();

        var renamed = await service.UpdateAsync(accountantId, Request("Bookkeeper", Permissions.Finance.JournalView));

        // The seeder finds system roles by name; a rename would make it
        // recreate the role on next startup.
        Assert.False(renamed.Succeeded);
        Assert.Equal(nameof(SaveRoleRequest.Name), renamed.Field);

        var repermissioned = await service.UpdateAsync(
            accountantId,
            Request(Roles.Accountant, Permissions.Finance.JournalView, Permissions.Sales.OrderView));

        Assert.True(repermissioned.Succeeded, repermissioned.Error);

        var detail = await service.GetAsync(accountantId);
        Assert.Contains(Permissions.Sales.OrderView, detail!.Permissions);
        Assert.DoesNotContain(Permissions.Finance.JournalPost, detail.Permissions);
    }

    [Fact]
    public async Task A_system_role_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var auditorId = await db.Roles.Where(r => r.Name == Roles.Auditor).Select(r => r.Id).SingleAsync();

        var result = await service.DeleteAsync(auditorId);

        Assert.False(result.Succeeded);
        Assert.Contains("system role", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_custom_role_can_be_created_and_deleted_while_unused()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RoleAdminService>();

        var created = await service.CreateAsync(
            Request("Stock Counter", Permissions.Inventory.StockView));

        Assert.True(created.Succeeded, created.Error);

        var detail = await service.GetAsync(created.Value);
        Assert.False(detail!.IsSystemRole);
        Assert.Equal([Permissions.Inventory.StockView], detail.Permissions);

        var deleted = await service.DeleteAsync(created.Value);
        Assert.True(deleted.Succeeded, deleted.Error);
    }

    [Fact]
    public async Task A_role_still_held_by_someone_cannot_be_deleted()
    {
        await using var scope = _fixture.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var created = await roles.CreateAsync(Request("Temp Role", Permissions.Sales.OrderView));
        Assert.True(created.Succeeded, created.Error);

        var user = await users.CreateAsync(new SaveStaffUserRequest
        {
            UserName = "holder-" + Guid.NewGuid().ToString("N")[..8],
            FullName = "Role Holder",
            Email = "holder@echolifestyle.test",
            Password = "Test!Passw0rd#2026",
            IsActive = true,
            Roles = ["Temp Role"],
            BranchIds = [_fixture.BranchId],
        });

        Assert.True(user.Succeeded, user.Error);

        var deleted = await roles.DeleteAsync(created.Value);

        Assert.False(deleted.Succeeded);
        Assert.Contains("still hold this role", deleted.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_permission_values_are_dropped_rather_than_stored()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RoleAdminService>();

        var created = await service.CreateAsync(
            Request("Odd Role", Permissions.Sales.OrderView, "Sales.Order.Teleport"));

        Assert.True(created.Succeeded, created.Error);

        var detail = await service.GetAsync(created.Value);

        // A stored claim nobody checks looks like a granted capability and is
        // never satisfied - worse than not storing it.
        Assert.Equal([Permissions.Sales.OrderView], detail!.Permissions);

        await service.DeleteAsync(created.Value);
    }

    [Fact]
    public async Task Changing_permissions_invalidates_the_sign_in_cookies_of_everyone_holding_the_role()
    {
        await using var scope = _fixture.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var created = await roles.CreateAsync(Request("Stamp Role", Permissions.Sales.OrderView));
        Assert.True(created.Succeeded, created.Error);

        var userName = "stamp-" + Guid.NewGuid().ToString("N")[..8];

        var user = await users.CreateAsync(new SaveStaffUserRequest
        {
            UserName = userName,
            FullName = "Stamp Person",
            Email = userName + "@echolifestyle.test",
            Password = "Test!Passw0rd#2026",
            IsActive = true,
            Roles = ["Stamp Role"],
            BranchIds = [_fixture.BranchId],
        });

        Assert.True(user.Succeeded, user.Error);

        var before = (await userManager.FindByNameAsync(userName))!.SecurityStamp;

        await roles.UpdateAsync(
            created.Value,
            Request("Stamp Role", Permissions.Sales.OrderView, Permissions.Inventory.StockView));

        var after = (await userManager.FindByNameAsync(userName))!.SecurityStamp;

        // A changed stamp is what makes an existing sign-in cookie stale, so a
        // revoked permission stops working without the person signing out.
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Editing_a_role_without_touching_permissions_leaves_stamps_alone()
    {
        await using var scope = _fixture.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleAdminService>();
        var users = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var created = await roles.CreateAsync(Request("Quiet Role", Permissions.Sales.OrderView));
        var userName = "quiet-" + Guid.NewGuid().ToString("N")[..8];

        await users.CreateAsync(new SaveStaffUserRequest
        {
            UserName = userName,
            FullName = "Quiet Person",
            Email = userName + "@echolifestyle.test",
            Password = "Test!Passw0rd#2026",
            IsActive = true,
            Roles = ["Quiet Role"],
            BranchIds = [_fixture.BranchId],
        });

        var before = (await userManager.FindByNameAsync(userName))!.SecurityStamp;

        // Description only - nobody's access changed, so nobody should be
        // forced to revalidate.
        await roles.UpdateAsync(created.Value, new SaveRoleRequest
        {
            Name = "Quiet Role",
            Description = "A different description",
            Permissions = [Permissions.Sales.OrderView],
        });

        var after = (await userManager.FindByNameAsync(userName))!.SecurityStamp;

        Assert.Equal(before, after);
    }
}
