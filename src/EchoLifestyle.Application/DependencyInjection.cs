using EchoLifestyle.Application.Administration.Auditing;
using EchoLifestyle.Application.Administration.Branches;
using EchoLifestyle.Application.Administration.CompanyProfile;
using EchoLifestyle.Application.Administration.Warehouses;
using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Finance;
using EchoLifestyle.Application.Finance.Cash;
using EchoLifestyle.Application.Finance.Remittances;
using EchoLifestyle.Application.Inventory;
using EchoLifestyle.Application.Inventory.Adjustments;
using EchoLifestyle.Application.Inventory.Counts;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Application.Sales.Pricing;
using EchoLifestyle.Application.Storefront;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the feature services that hold business rules. Everything here
    /// depends only on interfaces from this layer, so nothing in Application
    /// knows it is talking to SQL Server or to ASP.NET Identity.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<BranchAdminService>();
        services.AddScoped<WarehouseAdminService>();
        services.AddScoped<CompanyAdminService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<BrandAdminService>();
        services.AddScoped<CategoryAdminService>();
        services.AddScoped<ProductAdminService>();
        services.AddScoped<SupplierAdminService>();
        services.AddScoped<GoodsReceiptService>();
        services.AddScoped<StockQueryService>();
        services.AddScoped<StockBalanceRebuildService>();

        // The one writer every document type posts stock through. Registered
        // once so there is no second copy of the ledger-and-balance rule.
        services.AddScoped<StockMovementWriter>();
        services.AddScoped<StockAdjustmentService>();
        services.AddScoped<StockCountService>();
        services.AddScoped<CustomerAdminService>();
        services.AddScoped<StockReservationService>();
        services.AddScoped<PriceResolver>();
        services.AddScoped<SalesOrderService>();

        // The one writer every document records money through, so an order's
        // collected figure stays a projection rather than a typed-over number.
        services.AddScoped<CashTransactionWriter>();
        services.AddScoped<CourierRemittanceService>();
        services.AddScoped<CashService>();
        services.AddScoped<ExpenseCategoryService>();
        services.AddScoped<PartnerService>();

        // The public site's only reader. Separate from the back-office query
        // services because "what a customer may see" is a different rule, and
        // it belongs somewhere it can be read in full.
        services.AddScoped<StorefrontCatalogService>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<StorefrontTrackingService>();
        services.AddScoped<StorefrontBannerService>();
        services.AddScoped<Marketing.BannerAdminService>();

        // Read-only, and built from the orders themselves rather than from a
        // kept-up-to-date summary table. A stored total is a total that can
        // drift from the rows it claims to summarise.
        services.AddScoped<Reporting.SalesReportService>();
        services.AddScoped<Reporting.InventoryReportService>();
        services.AddScoped<Reporting.SpendReportService>();

        return services;
    }
}
