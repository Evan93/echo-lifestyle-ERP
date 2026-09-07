using System.Reflection;

namespace EchoLifestyle.Application.Common.Authorization;

/// <summary>
/// Action-based permissions. Authorization is always evaluated against these,
/// never against role names, so roles can be reshaped without touching code.
///
/// Naming: Module.Entity.Action
///
/// Every permission listed here is seeded as a role claim and enforced by a
/// matching authorization policy. Hiding a menu item is never the control -
/// the server check is.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Administration
    {
        public const string CompanyView = "Administration.Company.View";
        public const string CompanyEdit = "Administration.Company.Edit";
        public const string BranchView = "Administration.Branch.View";
        public const string BranchEdit = "Administration.Branch.Edit";
        public const string WarehouseView = "Administration.Warehouse.View";
        public const string WarehouseEdit = "Administration.Warehouse.Edit";
        public const string SettingsEdit = "Administration.Settings.Edit";
    }

    public static class Security
    {
        public const string UserView = "Security.User.View";
        public const string UserCreate = "Security.User.Create";
        public const string UserEdit = "Security.User.Edit";
        public const string UserLock = "Security.User.Lock";
        public const string UserResetPassword = "Security.User.ResetPassword";
        public const string RoleView = "Security.Role.View";
        public const string RoleEdit = "Security.Role.Edit";
        public const string RolePermissionsEdit = "Security.Role.PermissionsEdit";
        public const string AuditLogView = "Security.AuditLog.View";
    }

    public static class Catalog
    {
        public const string ProductView = "Catalog.Product.View";
        public const string ProductEdit = "Catalog.Product.Edit";

        /// <summary>
        /// Publishing to the storefront is separate from editing: a merchandiser
        /// may prepare a product without being able to put it in front of
        /// customers.
        /// </summary>
        public const string ProductPublish = "Catalog.Product.Publish";

        public const string BrandView = "Catalog.Brand.View";
        public const string BrandEdit = "Catalog.Brand.Edit";
        public const string CategoryView = "Catalog.Category.View";
        public const string CategoryEdit = "Catalog.Category.Edit";
        public const string UnitOfMeasureEdit = "Catalog.UnitOfMeasure.Edit";
        public const string CostPriceView = "Catalog.CostPrice.View";
        public const string PriceEdit = "Catalog.Price.Edit";
        public const string PriceListEdit = "Catalog.PriceList.Edit";
    }

    public static class Inventory
    {
        public const string StockView = "Inventory.Stock.View";
        public const string TransferCreate = "Inventory.Transfer.Create";
        public const string TransferApprove = "Inventory.Transfer.Approve";
        public const string AdjustmentCreate = "Inventory.Adjustment.Create";
        public const string AdjustmentApprove = "Inventory.Adjustment.Approve";

        /// <summary>Starting a count and entering the numbers found on the shelf.</summary>
        public const string CountCreate = "Inventory.Count.Create";

        /// <summary>
        /// Turning counted variances into stock movements. Separate from
        /// creating a count because counting is clerical and posting is not:
        /// the numbers become real, and the losses become permanent, here.
        /// </summary>
        public const string CountPost = "Inventory.Count.Post";

        /// <summary>
        /// Required in addition to the branch's AllowNegativeStock policy before
        /// a document may drive stock below zero.
        /// </summary>
        public const string AllowNegative = "Inventory.Stock.AllowNegative";

        /// <summary>
        /// Rebuilds the balance projection from the ledger. Harmless when the
        /// two already agree and the only way back when they do not - but it
        /// touches every balance, so it is not something a salesperson holds.
        /// </summary>
        public const string BalanceRebuild = "Inventory.Balance.Rebuild";

        public const string BatchView = "Inventory.Batch.View";
    }

    public static class Procurement
    {
        public const string SupplierView = "Procurement.Supplier.View";
        public const string SupplierEdit = "Procurement.Supplier.Edit";
        public const string PurchaseOrderCreate = "Procurement.PurchaseOrder.Create";
        public const string PurchaseOrderApprove = "Procurement.PurchaseOrder.Approve";
        public const string GoodsReceiptCreate = "Procurement.GoodsReceipt.Create";
    }

    public static class Sales
    {
        public const string OrderView = "Sales.Order.View";
        public const string OrderCreate = "Sales.Order.Create";
        public const string OrderCancel = "Sales.Order.Cancel";
        public const string DiscountApproveOverLimit = "Sales.Discount.ApproveOverLimit";
        public const string ReturnApprove = "Sales.Return.Approve";
    }

    public static class Finance
    {
        public const string JournalView = "Finance.Journal.View";
        public const string JournalPost = "Finance.Journal.Post";
        public const string PeriodClose = "Finance.Period.Close";
        public const string PeriodReopen = "Finance.Period.Reopen";

        /// <summary>Partner capital and drawings - Evan and Jaman only by default.</summary>
        public const string PartnerLedgerView = "Finance.PartnerLedger.View";
    }

    public static class Crm
    {
        public const string CustomerView = "Crm.Customer.View";
        public const string CustomerEdit = "Crm.Customer.Edit";

        /// <summary>Access to customer contact details; logged when exercised.</summary>
        public const string CustomerViewPii = "Crm.Customer.ViewPii";
    }

    public static class Reporting
    {
        public const string SalesReportsView = "Reporting.Sales.View";
        public const string InventoryReportsView = "Reporting.Inventory.View";
        public const string FinancialReportsView = "Reporting.Financial.View";
        public const string OwnerDashboardView = "Reporting.OwnerDashboard.View";
    }

    private static readonly Lazy<IReadOnlyList<string>> AllPermissions = new(Discover);

    /// <summary>Every permission constant declared above, discovered by reflection.</summary>
    public static IReadOnlyList<string> All => AllPermissions.Value;

    /// <summary>Permissions grouped by their module (the first dotted segment).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByModule =>
        All.GroupBy(p => p.Split('.')[0])
           .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.ToList());

    public static bool Exists(string permission) => All.Contains(permission);

    private static IReadOnlyList<string> Discover()
    {
        return typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
}
