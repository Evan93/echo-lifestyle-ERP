using EchoLifestyle.Application.Administration.Branches;
using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Branch administration rules.
///
/// Each test works inside its own company so the "last active branch" rule -
/// which counts within a company - is not affected by data other tests leave
/// behind.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class BranchAdminServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private long _companyId;

    public BranchAdminServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _fixture.CurrentUser.IsAuthenticated = true;
        _fixture.CurrentUser.UserType = UserType.Staff;
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.UserId = 1;
        _fixture.CurrentUser.UserName = "test-owner";

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var company = new Company { Name = $"Branch Rules Co {Guid.NewGuid():N}" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        _companyId = company.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Branch> AddBranchAsync(string code, bool isActive = true)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = new Branch
        {
            CompanyId = _companyId,
            Code = code,
            Name = code + " branch",
            IsActive = isActive,
        };

        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        return branch;
    }

    private static SaveBranchRequest Request(Branch branch, bool isActive) => new()
    {
        Code = branch.Code,
        Name = branch.Name,
        Type = branch.Type,
        IsActive = isActive,
        AllowNegativeStock = branch.AllowNegativeStock,
        Warehouses = [],
    };

    [Fact]
    public async Task Deactivating_the_only_active_branch_is_refused()
    {
        var branch = await AddBranchAsync("SOLO");

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var result = await service.UpdateAsync(branch.Id, Request(branch, isActive: false));

        Assert.False(result.Succeeded);
        Assert.Contains("only active branch", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(SaveBranchRequest.IsActive), result.Field);

        // And it really is still active - the refusal is not cosmetic.
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
        var reloaded = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == branch.Id);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Deactivating_one_of_two_active_branches_is_allowed()
    {
        var first = await AddBranchAsync("PAIR-A");
        await AddBranchAsync("PAIR-B");

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var result = await service.UpdateAsync(first.Id, Request(first, isActive: false));

        Assert.True(result.Succeeded);

        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
        var reloaded = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == first.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task An_inactive_branch_can_be_reactivated()
    {
        await AddBranchAsync("REACT-A");
        var dormant = await AddBranchAsync("REACT-B", isActive: false);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var result = await service.UpdateAsync(dormant.Id, Request(dormant, isActive: true));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_duplicate_code_is_reported_against_the_code_field()
    {
        await AddBranchAsync("TAKEN");
        var other = await AddBranchAsync("FREE");

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var request = Request(other, isActive: true);
        request.Code = "TAKEN";

        var result = await service.UpdateAsync(other.Id, request);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveBranchRequest.Code), result.Field);
    }

    [Fact]
    public async Task Two_primary_warehouses_are_refused_with_a_readable_message()
    {
        var branch = await AddBranchAsync("PRIMARIES");

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var first = new Warehouse { CompanyId = _companyId, Code = "PW-A", Name = "A" };
        var second = new Warehouse { CompanyId = _companyId, Code = "PW-B", Name = "B" };
        db.AddRange(first, second);
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var request = Request(branch, isActive: true);
        request.Warehouses =
        [
            new WarehouseAssignment { WarehouseId = first.Id, IsLinked = true, IsPrimary = true },
            new WarehouseAssignment { WarehouseId = second.Id, IsLinked = true, IsPrimary = true },
        ];

        var result = await service.UpdateAsync(branch.Id, request);

        // The database would refuse this too, but the user should get a
        // sentence rather than a constraint violation.
        Assert.False(result.Succeeded);
        Assert.Contains("primary warehouse", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Linking_warehouses_sets_the_primary_and_survives_a_reload()
    {
        var branch = await AddBranchAsync("LINKED");

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var warehouse = new Warehouse { CompanyId = _companyId, Code = "LW-A", Name = "Linked" };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var request = Request(branch, isActive: true);
        request.Warehouses =
        [
            new WarehouseAssignment
            {
                WarehouseId = warehouse.Id,
                IsLinked = true,
                IsPrimary = true,
                Priority = 5,
            },
        ];

        var result = await service.UpdateAsync(branch.Id, request);
        Assert.True(result.Succeeded);

        var detail = await service.GetAsync(branch.Id);
        Assert.NotNull(detail);

        var link = Assert.Single(detail!.Warehouses.Where(w => w.IsLinked));
        Assert.True(link.IsPrimary);
        Assert.Equal(5, link.Priority);
    }

    [Fact]
    public async Task The_status_filter_selects_active_inactive_or_all()
    {
        await AddBranchAsync("FILT-ON");
        await AddBranchAsync("FILT-OFF", isActive: false);

        await using var scope = _fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

        var active = await service.ListAsync("FILT-", 0, 50, null, false, BranchStatusFilter.Active);
        var inactive = await service.ListAsync("FILT-", 0, 50, null, false, BranchStatusFilter.Inactive);
        var all = await service.ListAsync("FILT-", 0, 50, null, false, BranchStatusFilter.All);

        Assert.Equal("FILT-ON", Assert.Single(active.Rows).Code);
        Assert.Equal("FILT-OFF", Assert.Single(inactive.Rows).Code);
        Assert.Equal(2, all.Rows.Count);
    }

    [Fact]
    public async Task A_non_owner_only_sees_branches_assigned_to_them()
    {
        var visible = await AddBranchAsync("SCOPE-IN");
        await AddBranchAsync("SCOPE-OUT");

        _fixture.CurrentUser.IsOwner = false;
        _fixture.CurrentUser.BranchIds = [visible.Id];

        try
        {
            await using var scope = _fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<BranchAdminService>();

            var page = await service.ListAsync("SCOPE-", 0, 50, null, false, BranchStatusFilter.All);

            Assert.Equal("SCOPE-IN", Assert.Single(page.Rows).Code);

            // And the one out of scope cannot be opened directly by id either.
            var branches = await service.ListAsync("SCOPE-OUT", 0, 50, null, false, BranchStatusFilter.All);
            Assert.Empty(branches.Rows);
        }
        finally
        {
            _fixture.CurrentUser.IsOwner = true;
            _fixture.CurrentUser.BranchIds = [];
        }
    }
}
