using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Auditing;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Purchasing;
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

    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }

    DbSet<Brand> Brands { get; }

    DbSet<Category> Categories { get; }

    DbSet<Product> Products { get; }

    DbSet<ProductCategory> ProductCategories { get; }

    DbSet<ProductOption> ProductOptions { get; }

    DbSet<ProductOptionValue> ProductOptionValues { get; }

    DbSet<ProductVariant> ProductVariants { get; }

    DbSet<ProductVariantOptionValue> ProductVariantOptionValues { get; }

    DbSet<ProductImage> ProductImages { get; }

    DbSet<PriceList> PriceLists { get; }

    DbSet<PriceListItem> PriceListItems { get; }

    DbSet<Supplier> Suppliers { get; }

    DbSet<StockBatch> StockBatches { get; }

    /// <summary>
    /// Append-only. Never updated, never deleted - the save interceptor refuses
    /// both. Corrections are reversing entries.
    /// </summary>
    DbSet<StockLedgerEntry> StockLedger { get; }

    /// <summary>
    /// A projection of the ledger. Only the inventory service writes here, and
    /// never without the ledger entry that justifies the change in the same
    /// transaction.
    /// </summary>
    DbSet<StockBalance> StockBalances { get; }

    DbSet<GoodsReceipt> GoodsReceipts { get; }

    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }

    DbSet<PurchaseCharge> PurchaseCharges { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the operation inside one database transaction, committing only if
    /// it completes.
    ///
    /// Posting a receipt writes a document, creates batches, appends ledger
    /// entries and moves balances. Half of that reaching the database would
    /// leave stock that exists in one place and not another, with no way to
    /// tell which is right - so it is one transaction or none of it.
    ///
    /// The implementation runs through the provider's execution strategy, which
    /// is what lets a transaction coexist with retry-on-failure. Doing this by
    /// hand with BeginTransaction throws as soon as retries are enabled, and
    /// only in the environment where they are.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
