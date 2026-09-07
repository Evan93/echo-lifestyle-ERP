namespace EchoLifestyle.Application.Common.Authorization;

/// <summary>
/// Seed roles. Roles are only a convenient bundle of permissions - all
/// enforcement is against permissions, so these can be edited freely in the
/// UI without code changes.
/// </summary>
public static class Roles
{
    public const string Owner = "Owner";
    public const string BusinessManager = "Business Manager";
    public const string FinanceManager = "Finance Manager";
    public const string Accountant = "Accountant";
    public const string StoreManager = "Store Manager";
    public const string Salesperson = "Salesperson";
    public const string WarehouseManager = "Warehouse Manager";
    public const string InventoryOfficer = "Inventory Officer";
    public const string PurchaseOfficer = "Purchase Officer";
    public const string CustomerService = "Customer Service";
    public const string MarketingManager = "Marketing Manager";
    public const string WebsiteManager = "Website Manager";
    public const string Auditor = "Auditor";

    /// <summary>
    /// Storefront shoppers. Held only by UserType.Customer accounts and
    /// carries no back-office permissions.
    /// </summary>
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> StaffRoles =
    [
        Owner,
        BusinessManager,
        FinanceManager,
        Accountant,
        StoreManager,
        Salesperson,
        WarehouseManager,
        InventoryOfficer,
        PurchaseOfficer,
        CustomerService,
        MarketingManager,
        WebsiteManager,
        Auditor,
    ];

    public static readonly IReadOnlyList<string> All = [.. StaffRoles, Customer];

    /// <summary>
    /// Default permission grants per role. Owner is handled separately and
    /// receives every permission, so that adding a new permission never
    /// silently locks the owners out of their own system.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> DefaultPermissions { get; } =
        new Dictionary<string, string[]>
        {
            [BusinessManager] =
            [
                Permissions.Administration.CompanyView,
                Permissions.Administration.BranchView,
                Permissions.Administration.WarehouseView,
                Permissions.Catalog.ProductView,
                Permissions.Catalog.ProductEdit,
                Permissions.Catalog.ProductPublish,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Catalog.BrandEdit,
                Permissions.Catalog.CategoryEdit,
                Permissions.Catalog.UnitOfMeasureEdit,
                Permissions.Catalog.CostPriceView,
                Permissions.Catalog.PriceEdit,
                Permissions.Catalog.PriceListEdit,
                Permissions.Inventory.StockView,
                Permissions.Inventory.BatchView,
                Permissions.Inventory.TransferApprove,
                Permissions.Inventory.AdjustmentApprove,
                Permissions.Inventory.CountPost,
                Permissions.Procurement.SupplierView,
                Permissions.Procurement.PurchaseOrderApprove,
                Permissions.Sales.OrderView,
                Permissions.Sales.OrderCancel,
                Permissions.Sales.OrderConfirm,
                Permissions.Sales.OrderDispatch,
                Permissions.Sales.DiscountApproveOverLimit,
                Permissions.Sales.ReturnApprove,
                Permissions.Finance.CashView,
                Permissions.Finance.RemittancePost,
                Permissions.Crm.CustomerView,
                Permissions.Crm.CustomerViewPii,
                Permissions.Crm.CustomerBlock,
                Permissions.Reporting.SalesReportsView,
                Permissions.Reporting.InventoryReportsView,
                Permissions.Reporting.FinancialReportsView,
                Permissions.Reporting.OwnerDashboardView,
            ],

            [FinanceManager] =
            [
                Permissions.Finance.CashView,
                Permissions.Finance.RemittancePost,
                Permissions.Finance.JournalView,
                Permissions.Finance.JournalPost,
                Permissions.Finance.PeriodClose,
                Permissions.Finance.PeriodReopen,
                Permissions.Procurement.PurchaseOrderApprove,
                Permissions.Sales.DiscountApproveOverLimit,
                Permissions.Catalog.CostPriceView,
                Permissions.Reporting.FinancialReportsView,
                Permissions.Reporting.SalesReportsView,
            ],

            [Accountant] =
            [
                Permissions.Finance.CashView,
                Permissions.Finance.RemittancePost,
                Permissions.Finance.JournalView,
                Permissions.Finance.JournalPost,
                Permissions.Catalog.CostPriceView,
                Permissions.Sales.ReturnApprove,
                Permissions.Reporting.FinancialReportsView,
            ],

            [StoreManager] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Inventory.StockView,
                Permissions.Inventory.TransferCreate,
                Permissions.Inventory.TransferApprove,
                Permissions.Inventory.AdjustmentCreate,
                Permissions.Inventory.CountCreate,
                Permissions.Sales.OrderView,
                Permissions.Sales.OrderCreate,
                Permissions.Sales.OrderConfirm,
                Permissions.Sales.OrderDispatch,
                Permissions.Sales.OrderCancel,
                Permissions.Sales.DiscountApproveOverLimit,
                Permissions.Sales.ReturnApprove,
                Permissions.Crm.CustomerView,
                Permissions.Crm.CustomerEdit,
                Permissions.Crm.CustomerViewPii,
                Permissions.Reporting.SalesReportsView,
            ],

            [Salesperson] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Inventory.StockView,
                Permissions.Sales.OrderView,
                Permissions.Sales.OrderCreate,
                Permissions.Sales.OrderConfirm,
                Permissions.Crm.CustomerView,
            ],

            [WarehouseManager] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Inventory.StockView,
                Permissions.Inventory.BatchView,
                Permissions.Inventory.TransferCreate,
                Permissions.Inventory.TransferApprove,
                Permissions.Inventory.AdjustmentCreate,
                Permissions.Inventory.AdjustmentApprove,
                Permissions.Inventory.CountCreate,
                Permissions.Inventory.CountPost,
                Permissions.Inventory.BalanceRebuild,
                Permissions.Procurement.GoodsReceiptCreate,

                // Packs and ships, but does not take or price orders.
                Permissions.Sales.OrderView,
                Permissions.Sales.OrderDispatch,
                Permissions.Reporting.InventoryReportsView,
            ],

            [InventoryOfficer] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Inventory.StockView,
                Permissions.Inventory.BatchView,
                Permissions.Inventory.TransferCreate,
                Permissions.Inventory.AdjustmentCreate,

                // Counts, but not posting them. An officer counts the shelf; an
                // owner decides that the difference is real and takes the loss.
                Permissions.Inventory.CountCreate,
                Permissions.Procurement.GoodsReceiptCreate,
                Permissions.Reporting.InventoryReportsView,
            ],

            [PurchaseOfficer] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Catalog.CostPriceView,
                Permissions.Inventory.StockView,
                Permissions.Inventory.BatchView,
                Permissions.Procurement.SupplierView,
                Permissions.Procurement.SupplierEdit,
                Permissions.Procurement.PurchaseOrderCreate,
                Permissions.Procurement.GoodsReceiptCreate,
            ],

            [CustomerService] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Inventory.StockView,
                Permissions.Sales.OrderView,
                Permissions.Sales.OrderCreate,
                Permissions.Sales.OrderConfirm,
                Permissions.Sales.OrderCancel,
                Permissions.Sales.ReturnApprove,
                Permissions.Crm.CustomerView,
                Permissions.Crm.CustomerEdit,
                Permissions.Crm.CustomerViewPii,
                Permissions.Crm.CustomerBlock,
            ],

            [MarketingManager] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Catalog.BrandEdit,
                Permissions.Catalog.CategoryEdit,
                Permissions.Crm.CustomerView,
                Permissions.Reporting.SalesReportsView,
            ],

            [WebsiteManager] =
            [
                Permissions.Catalog.ProductView,
                Permissions.Catalog.ProductEdit,
                Permissions.Catalog.ProductPublish,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Catalog.BrandEdit,
                Permissions.Catalog.CategoryEdit,
                Permissions.Catalog.PriceEdit,
                Permissions.Inventory.StockView,
                Permissions.Sales.OrderView,
            ],

            // Read-only across the business, including the audit trail.
            [Auditor] =
            [
                Permissions.Administration.CompanyView,
                Permissions.Administration.BranchView,
                Permissions.Administration.WarehouseView,
                Permissions.Security.AuditLogView,
                Permissions.Security.UserView,
                Permissions.Security.RoleView,
                Permissions.Catalog.ProductView,
                Permissions.Catalog.BrandView,
                Permissions.Catalog.CategoryView,
                Permissions.Catalog.CostPriceView,
                Permissions.Inventory.StockView,
                Permissions.Procurement.SupplierView,
                Permissions.Sales.OrderView,
                Permissions.Finance.CashView,
                Permissions.Finance.JournalView,
                Permissions.Reporting.SalesReportsView,
                Permissions.Reporting.InventoryReportsView,
                Permissions.Reporting.FinancialReportsView,
            ],

            // Deliberately empty: storefront access is granted by UserType,
            // not by back-office permissions.
            [Customer] = [],
        };
}
