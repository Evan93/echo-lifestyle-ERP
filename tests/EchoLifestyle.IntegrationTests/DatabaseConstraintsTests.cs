using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Proves the guarantees that live in the database rather than in C#.
///
/// These are the tests that justify running against real SQL Server: filtered
/// unique indexes, restricted deletes and soft-delete query filters simply do
/// not exist in an in-memory provider, so passing there would prove nothing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class DatabaseConstraintsTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private long _companyId;

    public DatabaseConstraintsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var company = new Company { Name = "Constraint Test Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        _companyId = company.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_branch_cannot_have_two_primary_warehouses()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = new Branch { CompanyId = _companyId, Code = "TWOPRIM", Name = "Two Primaries" };
        var first = new Warehouse { CompanyId = _companyId, Code = "WP1", Name = "WP1" };
        var second = new Warehouse { CompanyId = _companyId, Code = "WP2", Name = "WP2" };

        db.AddRange(branch, first, second);
        await db.SaveChangesAsync();

        db.BranchWarehouses.Add(new BranchWarehouse
        {
            BranchId = branch.Id,
            WarehouseId = first.Id,
            IsPrimary = true,
        });
        await db.SaveChangesAsync();

        db.BranchWarehouses.Add(new BranchWarehouse
        {
            BranchId = branch.Id,
            WarehouseId = second.Id,
            IsPrimary = true,
        });

        // The filtered unique index refuses this - application code cannot
        // accidentally leave a branch with two default fulfilment sources.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_same_warehouse_cannot_be_linked_to_a_branch_twice()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = new Branch { CompanyId = _companyId, Code = "DUPLINK", Name = "Duplicate Link" };
        var warehouse = new Warehouse { CompanyId = _companyId, Code = "WD1", Name = "WD1" };

        db.AddRange(branch, warehouse);
        await db.SaveChangesAsync();

        db.BranchWarehouses.Add(new BranchWarehouse { BranchId = branch.Id, WarehouseId = warehouse.Id });
        await db.SaveChangesAsync();

        db.BranchWarehouses.Add(new BranchWarehouse { BranchId = branch.Id, WarehouseId = warehouse.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Branch_codes_are_unique_within_a_company()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        db.Branches.Add(new Branch { CompanyId = _companyId, Code = "UNIQ", Name = "First" });
        await db.SaveChangesAsync();

        db.Branches.Add(new Branch { CompanyId = _companyId, Code = "UNIQ", Name = "Second" });

        // Codes print on documents; two branches sharing one would make a
        // document number ambiguous.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_retired_branch_frees_its_code_for_reuse()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var original = new Branch { CompanyId = _companyId, Code = "REUSE", Name = "Original" };
        db.Branches.Add(original);
        await db.SaveChangesAsync();

        db.Branches.Remove(original);
        await db.SaveChangesAsync();

        // The unique index is filtered on IsDeleted = 0, so this is allowed.
        db.Branches.Add(new Branch { CompanyId = _companyId, Code = "REUSE", Name = "Replacement" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Branches.CountAsync(b => b.Code == "REUSE"));
        Assert.Equal(2, await db.Branches.IgnoreQueryFilters().CountAsync(b => b.Code == "REUSE"));
    }

    [Fact]
    public async Task Removing_master_data_soft_deletes_it_rather_than_erasing_the_row()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = new Branch { CompanyId = _companyId, Code = "SOFTDEL", Name = "Soft Delete" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        db.Branches.Remove(branch);
        await db.SaveChangesAsync();

        Assert.Null(await db.Branches.FirstOrDefaultAsync(b => b.Id == branch.Id));

        var retired = await db.Branches
            .IgnoreQueryFilters()
            .SingleAsync(b => b.Id == branch.Id);

        Assert.True(retired.IsDeleted);
        Assert.NotNull(retired.DeletedAtUtc);
    }

    [Fact]
    public async Task A_warehouse_still_linked_to_a_branch_cannot_be_deleted_from_the_database()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = new Branch { CompanyId = _companyId, Code = "RESTRICT", Name = "Restrict" };
        var warehouse = new Warehouse { CompanyId = _companyId, Code = "WR1", Name = "WR1" };
        db.AddRange(branch, warehouse);
        await db.SaveChangesAsync();

        db.BranchWarehouses.Add(new BranchWarehouse { BranchId = branch.Id, WarehouseId = warehouse.Id });
        await db.SaveChangesAsync();

        // Deliberately raw SQL: this asserts the constraint exists in the
        // database, not merely that EF is configured to avoid the delete.
        // Nothing outside the application - a script, a DBA, a future job -
        // can quietly take dependent rows with it.
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "DELETE FROM admin.Warehouses WHERE Id = {0}", warehouse.Id));
    }

    [Fact]
    public async Task Concurrent_edits_to_the_same_row_are_refused()
    {
        await using var scopeOne = _fixture.CreateScope();
        await using var scopeTwo = _fixture.CreateScope();

        var dbOne = scopeOne.ServiceProvider.GetRequiredService<EchoDbContext>();
        var dbTwo = scopeTwo.ServiceProvider.GetRequiredService<EchoDbContext>();

        var branch = new Branch { CompanyId = _companyId, Code = "CONCUR", Name = "Concurrency" };
        dbOne.Branches.Add(branch);
        await dbOne.SaveChangesAsync();

        var first = await dbOne.Branches.SingleAsync(b => b.Id == branch.Id);
        var second = await dbTwo.Branches.SingleAsync(b => b.Id == branch.Id);

        first.Name = "Renamed by the first user";
        await dbOne.SaveChangesAsync();

        second.Name = "Renamed by the second user";

        // rowversion turns a silent lost update into an error the UI can
        // report. This matters far more once stock and money are involved.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbTwo.SaveChangesAsync());
    }
}
