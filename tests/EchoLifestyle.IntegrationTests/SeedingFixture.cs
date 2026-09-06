using EchoLifestyle.Application;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// An empty database for the seeding tests, and deliberately nothing else in
/// it.
///
/// Those tests assert about the state of a whole database - "there is one
/// company", "there are exactly two users", "seeding twice changes nothing".
/// Claims like that cannot survive sharing a database with classes that create
/// companies and users of their own: they would pass or fail on which class
/// xUnit happened to run first. Scoping the assertions instead would weaken the
/// exact thing being tested.
///
/// Migrations only. The tests run the seeder themselves - that is what they are
/// testing.
/// </summary>
public class SeedingFixture : IAsyncLifetime
{
    private const string DefaultConnection =
        "Server=localhost;Database=EchoLifestyle_SeedTests;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public ServiceProvider Services { get; private set; } = null!;

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ECHO_SEED_TEST_CONNECTION") ?? DefaultConnection;

    public TestCurrentUser CurrentUser { get; } = new();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Mirrors Program.cs. A fixture that registers less than the web host
        // passes tests that fail in production.
        services.AddDataProtection();
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
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<EchoDbContext>()
            .AddDefaultTokenProviders();

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
public class SeedingCollection : ICollectionFixture<SeedingFixture>
{
    public const string Name = "seeding";
}
