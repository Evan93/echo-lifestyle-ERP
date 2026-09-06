using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Auditing;
using EchoLifestyle.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Common.Interfaces;

/// <summary>
/// The database as the Application layer sees it.
///
/// Deliberately exposes EF Core's DbSet rather than hiding it behind a generic
/// repository: a repository that only forwards to EF Core adds a layer, removes
/// LINQ composition, and buys nothing. The interface exists so feature services
/// can live in Application - where the business rules belong - without the
/// Application project taking a dependency on SQL Server or on Identity.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Company> Companies { get; }

    DbSet<Branch> Branches { get; }

    DbSet<Warehouse> Warehouses { get; }

    DbSet<BranchWarehouse> BranchWarehouses { get; }

    DbSet<UserBranch> UserBranches { get; }

    DbSet<AuditLogEntry> AuditLog { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
