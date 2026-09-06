using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure.Auditing;
using EchoLifestyle.Infrastructure.Common;
using EchoLifestyle.Infrastructure.Files;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and infrastructure services.
    ///
    /// Takes a plain connection string rather than IConfiguration so this layer
    /// stays independent of how the host happens to be configured.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        bool isDevelopment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Registered explicitly rather than relying on AddLogging to have done
        // it: the file store resolves IOptions, and a bare ServiceCollection in
        // a test would otherwise fail depending on registration order.
        services.AddOptions();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // Staff administration lives here rather than in Application because
        // every operation goes through ASP.NET Identity's UserManager - see the
        // note on UserAdminService.
        services.AddScoped<Identity.UserAdminService>();
        services.AddScoped<Identity.RoleAdminService>();

        services.AddDbContext<EchoDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", EchoDbContext.AdminSchema);
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            });

            options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());

            if (isDevelopment)
            {
                // Helpful locally; never enabled in production because parameter
                // values would end up in the logs.
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            }
        });

        // The same context instance, seen through the Application layer's
        // interface, so feature services never reference Infrastructure.
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<EchoDbContext>());

        return services;
    }
}
