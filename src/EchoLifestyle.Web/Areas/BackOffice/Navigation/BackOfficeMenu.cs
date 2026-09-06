using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;

namespace EchoLifestyle.Web.Areas.BackOffice.Navigation;

/// <summary>
/// Builds the sidebar for the signed-in user.
///
/// Filtering here is a usability measure only - it stops people staring at
/// links they cannot use. Every action behind these links is authorized on the
/// server independently, so a hidden link is never the access control.
/// </summary>
public class BackOfficeMenu
{
    private readonly ICurrentUser _currentUser;

    public BackOfficeMenu(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public IReadOnlyList<NavItem> Build()
    {
        var all = Definition();
        return Filter(all);
    }

    private IReadOnlyList<NavItem> Filter(IReadOnlyList<NavItem> items)
    {
        var visible = new List<NavItem>();

        foreach (var item in items)
        {
            if (item.IsGroup)
            {
                var children = Filter(item.Children);
                if (children.Count == 0)
                {
                    continue;
                }

                visible.Add(new NavItem
                {
                    Text = item.Text,
                    Icon = item.Icon,
                    Permission = item.Permission,
                    ComingInPhase = item.ComingInPhase,
                    Children = children,
                });

                continue;
            }

            if (item.Permission is null || _currentUser.HasPermission(item.Permission))
            {
                visible.Add(item);
            }
        }

        return visible;
    }

    private static IReadOnlyList<NavItem> Definition() =>
    [
        new NavItem
        {
            Text = "Dashboard",
            Icon = "grid",
            Controller = "Dashboard",
            Action = "Index",
        },

        new NavItem
        {
            Text = "Catalog",
            Icon = "box",
            Children =
            [
                new NavItem
                {
                    Text = "Products",
                    Icon = "dot",
                    Controller = "Products",
                    Action = "Index",
                    Permission = Permissions.Catalog.ProductView,
                },
                new NavItem
                {
                    Text = "Brands",
                    Icon = "dot",
                    Controller = "Brands",
                    Action = "Index",
                    Permission = Permissions.Catalog.BrandView,
                },
                new NavItem
                {
                    Text = "Categories",
                    Icon = "dot",
                    Controller = "Categories",
                    Action = "Index",
                    Permission = Permissions.Catalog.CategoryView,
                },
                new NavItem { Text = "Price lists", Icon = "dot", Permission = Permissions.Catalog.PriceListEdit, ComingInPhase = "2" },
            ],
        },

        new NavItem
        {
            Text = "Procurement",
            Icon = "truck",
            Children =
            [
                new NavItem
                {
                    Text = "Suppliers",
                    Icon = "dot",
                    Controller = "Suppliers",
                    Action = "Index",
                    Permission = Permissions.Procurement.SupplierView,
                },
                new NavItem { Text = "Quick purchase", Icon = "dot", Permission = Permissions.Procurement.GoodsReceiptCreate, ComingInPhase = "2" },
                new NavItem { Text = "Purchase orders", Icon = "dot", Permission = Permissions.Procurement.PurchaseOrderCreate, ComingInPhase = "2" },
                new NavItem { Text = "Goods receipts", Icon = "dot", Permission = Permissions.Procurement.GoodsReceiptCreate, ComingInPhase = "2" },
            ],
        },

        new NavItem
        {
            Text = "Inventory",
            Icon = "layers",
            Children =
            [
                new NavItem { Text = "Stock on hand", Icon = "dot", Permission = Permissions.Inventory.StockView, ComingInPhase = "3" },
                new NavItem { Text = "Stock ledger", Icon = "dot", Permission = Permissions.Inventory.StockView, ComingInPhase = "3" },
                new NavItem { Text = "Transfers", Icon = "dot", Permission = Permissions.Inventory.TransferCreate, ComingInPhase = "3" },
                new NavItem { Text = "Adjustments", Icon = "dot", Permission = Permissions.Inventory.AdjustmentCreate, ComingInPhase = "3" },
                new NavItem { Text = "Near expiry", Icon = "dot", Permission = Permissions.Inventory.StockView, ComingInPhase = "3" },
            ],
        },

        new NavItem
        {
            Text = "Sales",
            Icon = "cart",
            Children =
            [
                new NavItem { Text = "Quick sale", Icon = "dot", Permission = Permissions.Sales.OrderCreate, ComingInPhase = "4" },
                new NavItem { Text = "Orders", Icon = "dot", Permission = Permissions.Sales.OrderView, ComingInPhase = "4" },
                new NavItem { Text = "Returns", Icon = "dot", Permission = Permissions.Sales.ReturnApprove, ComingInPhase = "4" },
                new NavItem { Text = "Deliveries", Icon = "dot", Permission = Permissions.Sales.OrderView, ComingInPhase = "4" },
            ],
        },

        new NavItem
        {
            Text = "Customers",
            Icon = "users",
            Children =
            [
                new NavItem { Text = "Customers", Icon = "dot", Permission = Permissions.Crm.CustomerView, ComingInPhase = "4" },
                new NavItem { Text = "Loyalty", Icon = "dot", Permission = Permissions.Crm.CustomerView, ComingInPhase = "7" },
            ],
        },

        new NavItem
        {
            Text = "Finance",
            Icon = "coins",
            Children =
            [
                new NavItem { Text = "Journals", Icon = "dot", Permission = Permissions.Finance.JournalView, ComingInPhase = "4" },
                new NavItem { Text = "Chart of accounts", Icon = "dot", Permission = Permissions.Finance.JournalView, ComingInPhase = "4" },
                new NavItem { Text = "Expenses", Icon = "dot", Permission = Permissions.Finance.JournalView, ComingInPhase = "4" },
                new NavItem { Text = "Partner capital", Icon = "dot", Permission = Permissions.Finance.PartnerLedgerView, ComingInPhase = "4" },
                new NavItem { Text = "Period close", Icon = "dot", Permission = Permissions.Finance.PeriodClose, ComingInPhase = "5" },
            ],
        },

        new NavItem
        {
            Text = "Reports",
            Icon = "chart",
            Children =
            [
                new NavItem { Text = "Sales reports", Icon = "dot", Permission = Permissions.Reporting.SalesReportsView, ComingInPhase = "4" },
                new NavItem { Text = "Inventory reports", Icon = "dot", Permission = Permissions.Reporting.InventoryReportsView, ComingInPhase = "3" },
                new NavItem { Text = "Financial reports", Icon = "dot", Permission = Permissions.Reporting.FinancialReportsView, ComingInPhase = "5" },
            ],
        },

        new NavItem
        {
            Text = "Administration",
            Icon = "settings",
            Children =
            [
                new NavItem
                {
                    Text = "Branches",
                    Icon = "dot",
                    Controller = "Branches",
                    Action = "Index",
                    Permission = Permissions.Administration.BranchView,
                },
                new NavItem
                {
                    Text = "Warehouses",
                    Icon = "dot",
                    Controller = "Warehouses",
                    Action = "Index",
                    Permission = Permissions.Administration.WarehouseView,
                },
                new NavItem
                {
                    Text = "Users",
                    Icon = "dot",
                    Controller = "Users",
                    Action = "Index",
                    Permission = Permissions.Security.UserView,
                },
                new NavItem
                {
                    Text = "Roles & permissions",
                    Icon = "dot",
                    Controller = "Roles",
                    Action = "Index",
                    Permission = Permissions.Security.RoleView,
                },
                new NavItem
                {
                    Text = "Audit trail",
                    Icon = "dot",
                    Controller = "Audit",
                    Action = "Index",
                    Permission = Permissions.Security.AuditLogView,
                },
                new NavItem
                {
                    Text = "Company",
                    Icon = "dot",
                    Controller = "Company",
                    Action = "Index",
                    Permission = Permissions.Administration.CompanyView,
                },
            ],
        },
    ];
}
