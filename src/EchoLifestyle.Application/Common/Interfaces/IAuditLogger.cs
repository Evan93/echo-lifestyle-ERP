namespace EchoLifestyle.Application.Common.Interfaces;

/// <summary>
/// Writes entries to the audit trail for security- and business-sensitive
/// actions. Called explicitly by use cases rather than blanket-logging every
/// row change, so the trail stays small enough that people actually read it.
/// </summary>
public interface IAuditLogger
{
    /// <param name="action">Dotted action key, e.g. "Security.Role.PermissionsChanged".</param>
    /// <param name="entityName">Logical entity affected, e.g. "ApplicationUser".</param>
    /// <param name="entityId">Key of the affected entity, as text.</param>
    /// <param name="summary">Human-readable one-line description.</param>
    /// <param name="detail">Anything structured worth keeping - before/after, reason, override justification.</param>
    /// <param name="branchId">Branch scope of the action, when applicable.</param>
    Task LogAsync(
        string action,
        string? entityName = null,
        string? entityId = null,
        string? summary = null,
        object? detail = null,
        long? branchId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Well-known audit action keys.</summary>
public static class AuditActions
{
    public const string UserCreated = "Security.User.Created";
    public const string UserLocked = "Security.User.Locked";
    public const string UserUnlocked = "Security.User.Unlocked";
    public const string UserRolesChanged = "Security.User.RolesChanged";
    public const string UserPasswordReset = "Security.User.PasswordReset";
    public const string RoleCreated = "Security.Role.Created";
    public const string RoleDeleted = "Security.Role.Deleted";
    public const string RolePermissionsChanged = "Security.Role.PermissionsChanged";
    public const string LoginSucceeded = "Security.Login.Succeeded";
    public const string LoginFailed = "Security.Login.Failed";
    public const string LoginLockedOut = "Security.Login.LockedOut";
    public const string CompanyUpdated = "Administration.Company.Updated";
    public const string BranchCreated = "Administration.Branch.Created";
    public const string BranchUpdated = "Administration.Branch.Updated";
    public const string WarehouseCreated = "Administration.Warehouse.Created";
    public const string WarehouseUpdated = "Administration.Warehouse.Updated";
    public const string BrandCreated = "Catalog.Brand.Created";
    public const string BrandUpdated = "Catalog.Brand.Updated";
    public const string CategoryCreated = "Catalog.Category.Created";
    public const string CategoryUpdated = "Catalog.Category.Updated";

    /// <summary>Re-parenting rewrites the paths of an entire subtree; worth its own key.</summary>
    public const string CategoryMoved = "Catalog.Category.Moved";

    public const string ProductCreated = "Catalog.Product.Created";
    public const string ProductUpdated = "Catalog.Product.Updated";
    public const string ProductPublished = "Catalog.Product.Published";
    public const string ProductUnpublished = "Catalog.Product.Unpublished";
    public const string VariantCreated = "Catalog.Variant.Created";
    public const string VariantDeactivated = "Catalog.Variant.Deactivated";

    /// <summary>Every selling-price change, with the old and new figure.</summary>
    public const string PriceChanged = "Catalog.Price.Changed";

    public const string SupplierCreated = "Procurement.Supplier.Created";
    public const string SupplierUpdated = "Procurement.Supplier.Updated";

    /// <summary>
    /// Stock arriving. Carries the landed cost of every line, because "why did
    /// this item cost that much" is asked months later, long after the invoice
    /// has been filed somewhere nobody remembers.
    /// </summary>
    public const string GoodsReceiptPosted = "Procurement.GoodsReceipt.Posted";

    /// <summary>
    /// Rebuilding balances from the ledger. Logged with the before and after
    /// figures for anything that changed, because a rebuild that silently
    /// corrects a number is exactly the event someone will later need to find.
    /// </summary>
    public const string StockBalanceRebuilt = "Inventory.Balance.Rebuilt";

    /// <summary>
    /// An adjustment written and waiting for approval. Logged before anything
    /// moves, so a request that is later turned down still leaves a trace of
    /// having been asked for.
    /// </summary>
    public const string StockAdjustmentSubmitted = "Inventory.Adjustment.Submitted";

    /// <summary>
    /// Approved and posted. The one movement with no document from outside the
    /// business behind it, so the entry carries the reason, the note, every line
    /// and whether the approver was also the author.
    /// </summary>
    public const string StockAdjustmentPosted = "Inventory.Adjustment.Posted";

    public const string StockAdjustmentRejected = "Inventory.Adjustment.Rejected";

    public const string StockCountStarted = "Inventory.Count.Started";

    /// <summary>
    /// Variances turned into movements. Carries the counted and system figures
    /// and the value of the difference - the number somebody will want explained
    /// at the end of the year.
    /// </summary>
    public const string StockCountPosted = "Inventory.Count.Posted";

    public const string StockCountCancelled = "Inventory.Count.Cancelled";
}
