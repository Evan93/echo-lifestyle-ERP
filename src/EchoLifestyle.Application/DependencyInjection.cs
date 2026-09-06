using EchoLifestyle.Application.Administration.Branches;
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

        return services;
    }
}
