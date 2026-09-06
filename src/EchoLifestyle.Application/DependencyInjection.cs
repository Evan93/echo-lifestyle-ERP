using EchoLifestyle.Application.Administration.Auditing;
using EchoLifestyle.Application.Administration.Branches;
using EchoLifestyle.Application.Administration.CompanyProfile;
using EchoLifestyle.Application.Administration.Warehouses;
using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Catalog.Categories;
using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
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

        return services;
    }
}
