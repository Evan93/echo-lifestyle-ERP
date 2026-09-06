using EchoLifestyle.Application;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// A real SQL Server database, created from the migrations and thrown away
/// afterwards.
///
/// Deliberately not an in-memory provider: filtered unique indexes, rowversion
/// concurrency and delete behaviour are the things most worth testing here, and
/// none of them exist in an in-memory store. A green in-memory test would prove
/// nothing about the database this system actually runs on.
///
/// Override the server with ECHO_TEST_CONNECTION if yours is not the default
/// local instance.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private const string DefaultConnection =
        "Server=localhost;Database=EchoLifestyle_Tests;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public ServiceProvider Services { get; private set; } = null!;

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ECHO_TEST_CONNECTION") ?? DefaultConnection;

    public TestCurrentUser CurrentUser { get; } = new();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddApplication();
        services.AddInfrastructure(ConnectionString, isDevelopment: true);

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 4;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<EchoDbContext>();

        services.AddScoped<DbSeeder>();

        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await Services.DisposeAsync();
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();
}

[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
