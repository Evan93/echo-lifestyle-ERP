using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Auditing;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Purchasing;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    public const string CatalogSchema = "catalog";
    public const string InventorySchema = "inventory";
    public const string PurchasingSchema = "purchasing";
    public const string CrmSchema = "crm";
    public const string SalesSchema = "sales";
    public const string FinanceSchema = "finance";

    public EchoDbContext(DbContextOptions<EchoDbContext> options)
        : base(options)
    {
        // Soft delete is implemented by AuditableEntityInterceptor turning a
        // Deleted entry into a Modified one at save time. By default EF cascades
        // the moment Remove() is called - long before the interceptor gets a
        // say - so removing a product whose variants happened to be loaded threw
        // "the association has been severed" instead of soft-deleting it. The
        // exception surfaced only when the dependents were tracked, which is to
        // say: not in a small test, and always in the editor that just loaded
        // them.
        //
        // Deferring both to save time lets the interceptor demote the delete
        // first, so nothing is left to cascade. Business foreign keys are
        // Restrict regardless (see ApplyConventions), so this changes when the
        // check happens, never whether it happens.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<BranchWarehouse> BranchWarehouses => Set<BranchWarehouse>();

    public DbSet<UserBranch> UserBranches => Set<UserBranch>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<ProductOption> ProductOptions => Set<ProductOption>();

    public DbSet<ProductOptionValue> ProductOptionValues => Set<ProductOptionValue>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductVariantOptionValue> ProductVariantOptionValues => Set<ProductVariantOptionValue>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();

    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<StockBatch> StockBatches => Set<StockBatch>();

    public DbSet<StockLedgerEntry> StockLedger => Set<StockLedgerEntry>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();

    public DbSet<StockCount> StockCounts => Set<StockCount>();

    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();

    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();

    public DbSet<PurchaseCharge> PurchaseCharges => Set<PurchaseCharge>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    public DbSet<Division> Divisions => Set<Division>();

    public DbSet<District> Districts => Set<District>();

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<SalesOrderStatusChange> SalesOrderStatusChanges => Set<SalesOrderStatusChange>();

    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<Partner> Partners => Set<Partner>();

    public DbSet<CourierRemittance> CourierRemittances => Set<CourierRemittance>();

    public DbSet<CourierRemittanceLine> CourierRemittanceLines => Set<CourierRemittanceLine>();

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Through the execution strategy, not around it. Retry-on-failure is
        // enabled on this context, and a hand-rolled BeginTransaction throws the
        // moment it is - a failure that would appear in production and nowhere
        // else. The strategy owns the retry loop and re-runs the whole
        // operation, transaction included.
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            cancellationToken,
            async (token) =>
            {
                await using var transaction = await Database.BeginTransactionAsync(token);

                var result = await operation(token);

                await transaction.CommitAsync(token);

                return result;
            });
    }

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
        builder.Entity<Brand>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Category>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Product>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Supplier>().HasQueryFilter(e => !e.IsDeleted);
        builder.Entity<Customer>().HasQueryFilter(e => !e.IsDeleted);

        // Addresses are not soft-deletable themselves, but a deleted customer's
        // addresses must not surface in a district report or a courier export.
        // Filtering through the parent is the only way to get that without
        // every caller remembering to join.
        builder.Entity<CustomerAddress>().HasQueryFilter(a => !a.Customer!.IsDeleted);

        // Variants are not soft-deletable themselves, but they are queried
        // directly all over the system - variant pickers, barcode lookups,
        // stock reads - and a variant of a deleted product must not surface in
        // any of them. Filtering through the parent is the only way to get that
        // without every caller remembering to join.
        builder.Entity<ProductVariant>().HasQueryFilter(v => !v.Product!.IsDeleted);
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
