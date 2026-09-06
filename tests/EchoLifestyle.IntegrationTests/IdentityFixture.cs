using EchoLifestyle.Application;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Security;
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
/// A separate database for the user and role tests.
///
/// These assert on questions like "is this the last active Owner?", which are
/// answered by counting rows across the whole database. Sharing a database with
/// the other test classes would make the answer depend on which class ran first
/// - the same trap that made the seeding counts fragile. Roles and the company
/// are seeded here; no user accounts are, so each test controls exactly who
/// exists.
/// </summary>
public class IdentityFixture : IAsyncLifetime
{
    private const string DefaultConnection =
        "Server=localhost;Database=EchoLifestyle_IdentityTests;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public ServiceProvider Services { get; private set; } = null!;

    public TestCurrentUser CurrentUser { get; } = new()
    {
        IsAuthenticated = true,
        UserType = UserType.Staff,
        IsOwner = true,
        UserId = 0,
        UserName = "test-admin",
    };

    public long BranchId { get; private set; }

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ECHO_IDENTITY_TEST_CONNECTION") ?? DefaultConnection;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

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

        // Roles, company, branch and warehouse - but deliberately no owners.
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(new SeedOptions { Owners = [] }, isDevelopment: true);

        BranchId = await db.Branches.Select(b => b.Id).FirstAsync();
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
public class IdentityCollection : ICollectionFixture<IdentityFixture>
{
    public const string Name = "identity";
}
