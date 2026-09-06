using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// The guards that stop the business locking itself out of its own system.
/// These matter more than the CRUD around them.
/// </summary>
[Collection(IdentityCollection.Name)]
public class UserAdminServiceTests : IAsyncLifetime
{
    private const string Password = "Test!Passw0rd#2026";

    private readonly IdentityFixture _fixture;

    public UserAdminServiceTests(IdentityFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Start each test from a clean slate of accounts so "the last Owner"
        // means what it says.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        await db.Database.ExecuteSqlRawAsync("DELETE FROM security.UserRoles");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM security.UserBranches");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM security.Users");

        _fixture.CurrentUser.UserId = 0;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SaveStaffUserRequest Request(
        string userName,
        string[]? roles = null,
        bool isActive = true,
        bool withBranch = true) => new()
        {
            UserName = userName,
            FullName = userName + " Person",
            Email = userName + "@echolifestyle.test",
            IsActive = isActive,
            Password = Password,
            Roles = [.. roles ?? []],
            BranchIds = withBranch ? [_fixture.BranchId] : [],
        };

    private async Task<long> CreateAsync(string userName, params string[] roles)
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await service.CreateAsync(Request(userName, roles));

        Assert.True(result.Succeeded, result.Error);
        return result.Value;
    }

    [Fact]
    public async Task A_created_account_is_staff_and_never_a_customer()
    {
        var id = await CreateAsync("newstaff", Roles.Salesperson);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);

        // The whole UserType design rests on this: nothing this screen creates
        // can be a customer, and nothing it edits can become one.
        Assert.Equal(UserType.Staff, user.UserType);
    }

    [Fact]
    public async Task An_account_with_no_branch_and_no_owner_role_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await service.CreateAsync(
            Request("nobranch", [Roles.Salesperson], withBranch: false));

        // Such an account signs in and sees nothing, which reads as a broken
        // system rather than a misconfiguration.
        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveStaffUserRequest.BranchIds), result.Field);
    }

    [Fact]
    public async Task An_owner_needs_no_branch_because_owners_see_everything()
    {
        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await service.CreateAsync(
            Request("ownernobranch", [Roles.Owner], withBranch: false));

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task The_last_active_owner_cannot_lose_the_owner_role()
    {
        var id = await CreateAsync("soleowner", Roles.Owner);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var request = Request("soleowner", [Roles.Accountant]);
        var result = await service.UpdateAsync(id, request);

        Assert.False(result.Succeeded);
        Assert.Contains("only active Owner", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(SaveStaffUserRequest.Roles), result.Field);

        // Still an Owner afterwards - the refusal is not cosmetic.
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
        var stillOwner = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == id && x.Name == Roles.Owner);

        Assert.True(stillOwner);
    }

    [Fact]
    public async Task The_last_active_owner_cannot_be_deactivated()
    {
        var id = await CreateAsync("lastowner", Roles.Owner);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await service.UpdateAsync(id, Request("lastowner", [Roles.Owner], isActive: false));

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveStaffUserRequest.IsActive), result.Field);
    }

    [Fact]
    public async Task An_owner_can_step_down_once_another_active_owner_exists()
    {
        var first = await CreateAsync("owner-a", Roles.Owner);
        await CreateAsync("owner-b", Roles.Owner);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var result = await service.UpdateAsync(first, Request("owner-a", [Roles.Accountant]));

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task A_deactivated_owner_does_not_count_toward_the_guard()
    {
        var active = await CreateAsync("active-owner", Roles.Owner);
        var dormant = await CreateAsync("dormant-owner", Roles.Owner);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        // Park the second owner.
        var parked = await service.UpdateAsync(
            dormant, Request("dormant-owner", [Roles.Owner], isActive: false));
        Assert.True(parked.Succeeded, parked.Error);

        // The remaining active owner is now the last one, so this must fail.
        var result = await service.UpdateAsync(active, Request("active-owner", [Roles.Accountant]));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task You_cannot_deactivate_your_own_account()
    {
        var id = await CreateAsync("self", Roles.Owner);
        await CreateAsync("other-owner", Roles.Owner);

        _fixture.CurrentUser.UserId = id;

        try
        {
            await using var scope = _fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

            var result = await service.UpdateAsync(id, Request("self", [Roles.Owner], isActive: false));

            // Even with another owner available, signing yourself out of a
            // system you administer is refused.
            Assert.False(result.Succeeded);
            Assert.Contains("your own account", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _fixture.CurrentUser.UserId = 0;
        }
    }

    [Fact]
    public async Task An_unknown_role_is_refused_rather_than_ignored()
    {
        var id = await CreateAsync("rolecheck", Roles.Salesperson);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var request = Request("rolecheck");
        request.Roles = ["Wizard"];

        var result = await service.UpdateAsync(id, request);

        Assert.False(result.Succeeded);
        Assert.Contains("Unknown role", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_customer_role_cannot_be_granted_from_the_staff_screen()
    {
        var id = await CreateAsync("noshopper", Roles.Salesperson);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        var request = Request("noshopper");
        request.Roles = [Roles.Customer];

        // Customer is not a staff role, so it is not assignable here at all.
        var result = await service.UpdateAsync(id, request);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Customer_accounts_never_appear_in_the_staff_list()
    {
        await CreateAsync("staffer", Roles.Salesperson);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        db.Users.Add(new ApplicationUser
        {
            UserName = "shopper",
            NormalizedUserName = "SHOPPER",
            Email = "shopper@example.test",
            NormalizedEmail = "SHOPPER@EXAMPLE.TEST",
            FullName = "A Shopper",
            UserType = UserType.Customer,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var page = await service.ListAsync(null, 0, 50, null, false, StatusFilter.All);

        Assert.Contains(page.Rows, r => r.UserName == "staffer");
        Assert.DoesNotContain(page.Rows, r => r.UserName == "shopper");
    }

    [Fact]
    public async Task A_password_reset_sets_a_working_password()
    {
        var id = await CreateAsync("resetme", Roles.Salesperson);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        var userManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();

        const string NewPassword = "Fresh!Passw0rd#2026";

        var result = await service.ResetPasswordAsync(id, NewPassword);
        Assert.True(result.Succeeded, result.Error);

        var user = await userManager.FindByIdAsync(id.ToString());
        Assert.True(await userManager.CheckPasswordAsync(user!, NewPassword));
        Assert.False(await userManager.CheckPasswordAsync(user!, Password));
    }

    [Fact]
    public async Task Assigning_branches_records_exactly_one_default()
    {
        var id = await CreateAsync("branchuser", Roles.Salesperson);

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var assignments = await db.UserBranches.Where(ub => ub.UserId == id).ToListAsync();

        var assignment = Assert.Single(assignments);
        Assert.Equal(_fixture.BranchId, assignment.BranchId);
        Assert.True(assignment.IsDefault);
    }
}
