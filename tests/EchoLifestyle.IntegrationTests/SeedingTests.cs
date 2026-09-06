using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class SeedingTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public SeedingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static SeedOptions Options() => new()
    {
        CompanyName = "Echo Lifestyle",
        BranchCode = "ONLINE",
        BranchName = "Online Store",
        WarehouseCode = "MAIN",
        WarehouseName = "Main Warehouse",
        Owners =
        [
            new OwnerSeed
            {
                UserName = "evan",
                Email = "evan@echolifestyle.test",
                FullName = "Evan Rahman",
                Password = "Seed!Passw0rd#2026",
            },
            new OwnerSeed
            {
                UserName = "jaman",
                Email = "jaman@echolifestyle.test",
                FullName = "Jaman",
                Password = "Seed!Passw0rd#2026b",
            },
        ],
    };

    public async Task InitializeAsync()
    {
        await using var scope = _fixture.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(Options(), isDevelopment: true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_anything()
    {
        // The seeder runs on every startup, so it has to be safe to repeat.
        await using var scope = _fixture.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(Options(), isDevelopment: true);

        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        // Counts are scoped to the seeded company. Other test classes in this
        // collection create their own companies and branches, so a global count
        // here would pass or fail depending on which class happened to run first.
        var company = await db.Companies.SingleAsync(c => c.Name == "Echo Lifestyle");

        Assert.Equal(1, await db.Companies.CountAsync(c => c.Name == "Echo Lifestyle"));
        Assert.Equal(1, await db.Branches.CountAsync(b => b.CompanyId == company.Id));
        Assert.Equal(1, await db.Warehouses.CountAsync(w => w.CompanyId == company.Id));
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(Roles.All.Count, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task Owner_role_holds_every_permission_that_exists()
    {
        // Guards the promise that adding a permission cannot lock the owners
        // out of their own system.
        await using var scope = _fixture.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var owner = await roleManager.FindByNameAsync(Roles.Owner);
        Assert.NotNull(owner);

        var granted = (await roleManager.GetClaimsAsync(owner!))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = Permissions.All.Where(p => !granted.Contains(p)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task Customer_role_is_seeded_with_no_permissions()
    {
        await using var scope = _fixture.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var customer = await roleManager.FindByNameAsync(Roles.Customer);
        Assert.NotNull(customer);

        var claims = await roleManager.GetClaimsAsync(customer!);
        Assert.DoesNotContain(claims, c => c.Type == Permissions.ClaimType);
    }

    [Fact]
    public async Task Owners_are_staff_accounts_in_the_owner_role()
    {
        await using var scope = _fixture.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var userName in new[] { "evan", "jaman" })
        {
            var user = await userManager.FindByNameAsync(userName);

            Assert.NotNull(user);
            Assert.Equal(UserType.Staff, user!.UserType);
            Assert.True(user.IsActive);
            Assert.True(await userManager.IsInRoleAsync(user, Roles.Owner));
        }
    }

    [Fact]
    public async Task Each_owner_is_assigned_the_seeded_branch_as_their_default()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var branch = await db.Branches.SingleAsync(b => b.Code == "ONLINE");
        var evan = await userManager.FindByNameAsync("evan");

        var assignments = await db.UserBranches
            .Where(ub => ub.UserId == evan!.Id)
            .ToListAsync();

        var assignment = Assert.Single(assignments);
        Assert.Equal(branch.Id, assignment.BranchId);
        Assert.True(assignment.IsDefault);
    }

    [Fact]
    public async Task Seeded_branch_refuses_negative_stock_by_default()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = await db.Branches.SingleAsync(b => b.Code == "ONLINE");

        Assert.False(branch.AllowNegativeStock);
        Assert.Equal(BranchType.Online, branch.Type);
    }

    [Fact]
    public async Task Company_defaults_to_BDT_and_Dhaka_with_no_vat_registration()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var company = await db.Companies.SingleAsync();

        Assert.Equal("BDT", company.BaseCurrencyCode);
        Assert.Equal("Asia/Dhaka", company.BusinessTimeZoneId);
        Assert.Equal("BD", company.CountryCode);

        // Not NBR-registered yet: VAT features stay dormant until this is set.
        Assert.Null(company.VatRegistrationNumber);
    }

    [Fact]
    public async Task Audit_fields_are_stamped_on_insert()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var company = await db.Companies.SingleAsync();

        Assert.NotEqual(default, company.CreatedAtUtc);
        Assert.True(company.CreatedAtUtc <= DateTime.UtcNow);
        Assert.NotNull(company.RowVersion);
    }
}
