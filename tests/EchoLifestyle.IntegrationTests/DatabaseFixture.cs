using EchoLifestyle.Application;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure;
using EchoLifestyle.Infrastructure.Files;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
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

    /// <summary>
    /// A throwaway directory for uploaded files, so the image tests exercise the
    /// real file store. The parts most worth testing there - the signature check
    /// and the traversal guard - do not exist in a stub.
    /// </summary>
    public string FileRoot { get; } = Path.Combine(
        Path.GetTempPath(), "echo-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.Configure<FileStorageOptions>(options => options.RootPath = FileRoot);

        // A web host registers data protection for us; a bare ServiceCollection
        // does not. Identity's password-reset token provider depends on it, so
        // without this every service resolution here fails.
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
                options.Password.RequiredUniqueChars = 4;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<EchoDbContext>()

            // Mirrors Program.cs. Password reset generates a token, which needs
            // these registered - without them the fixture would pass tests that
            // fail in production.
            .AddDefaultTokenProviders();

        services.AddScoped<DbSeeder>();

        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        // Seeded here, once, rather than left to whichever test class runs
        // first.
        //
        // Catalogue tests need the units of measure and the default price list;
        // before this, those existed only because SeedingTests happened to run
        // earlier in the same collection and seed them as a side effect. That is
        // not a dependency any test declared, and it broke the moment a new
        // class changed the running order.
        //
        // No owner accounts: classes that care about users create exactly the
        // ones they need.
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(new SeedOptions { Owners = [] }, isDevelopment: true);
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await Services.DisposeAsync();

        if (Directory.Exists(FileRoot))
        {
            Directory.Delete(FileRoot, recursive: true);
        }
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();
}

[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
