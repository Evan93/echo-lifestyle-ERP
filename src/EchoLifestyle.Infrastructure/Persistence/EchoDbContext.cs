using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Auditing;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Infrastructure.Persistence;

/// <summary>
/// The single application DbContext. One database is the source of truth for
/// back-office, POS and storefront alike - there is deliberately no separate
/// website catalogue or stock table to drift out of sync.
///
/// Tables are grouped into SQL Server schemas by module so ownership stays
/// visible as the system grows.
/// </summary>
public class EchoDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, long>, IApplicationDbContext
{
    public const string AdminSchema = "admin";
    public const string SecuritySchema = "security";
    public const string AuditSchema = "audit";

    public EchoDbContext(DbContextOptions<EchoDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<BranchWarehouse> BranchWarehouses => Set<BranchWarehouse>();

    public DbSet<UserBranch> UserBranches => Set<UserBranch>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(EchoDbContext).Assembly);

        ConfigureIdentityTables(builder);
        ApplySoftDeleteFilters(builder);
        ApplyConventions(builder);
    }

    /// <summary>
    /// Identity's default table names (AspNetUsers etc.) are renamed into the
    /// security schema so the database reads consistently.
    /// </summary>
    private static void ConfigureIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("Users", SecuritySchema);
            b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            b.Property(u => u.Notes).HasMaxLength(500);
            b.Property(u => u.UserType).HasConversion<int>();
            b.HasIndex(u => u.UserType);
            b.HasIndex(u => new { u.UserType, u.IsActive });
        });

        builder.Entity<ApplicationRole>(b =>
        {
            b.ToTable("Roles", SecuritySchema);
            b.Property(r => r.Description).HasMaxLength(500);
        });

        builder.Entity<IdentityUserRole<long>>().ToTable("UserRoles", SecuritySchema);
        builder.Entity<IdentityUserClaim<long>>().ToTable("UserClaims", SecuritySchema);
        builder.Entity<IdentityUserLogin<long>>().ToTable("UserLogins", SecuritySchema);
        builder.Entity<IdentityRoleClaim<long>>().ToTable("RoleClaims", SecuritySchema);
        builder.Entity<IdentityUserToken<long>>().ToTable("UserTokens", SecuritySchema);
    }

    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    {
        builder.Entity<Company>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Branch>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Warehouse>().HasQueryFilter(e => !e.IsDeleted);
    }

    /// <summary>
    /// Model-wide conventions: rowversion concurrency tokens and a guard
    /// against accidental cascade deletes on posted data.
    /// </summary>
    private static void ApplyConventions(ModelBuilder builder)
    {
        // Materialised first: configuring an entity can touch the model, and
        // enumerating it live while doing so is not safe.
        foreach (var entityType in builder.Model.GetEntityTypes().ToList())
        {
            // Identity's own join tables are left alone: their cascade
            // behaviour is part of how ASP.NET Identity works.
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            builder.Entity(entityType.ClrType)
                .Property(nameof(BaseEntity.RowVersion))
                .IsRowVersion();

            // Business data is never cascade-deleted. Removing a branch must
            // fail loudly rather than silently taking its documents with it.
            foreach (var fk in entityType.GetForeignKeys())
            {
                if (fk.DeleteBehavior == DeleteBehavior.Cascade)
                {
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }
        }
    }
}
