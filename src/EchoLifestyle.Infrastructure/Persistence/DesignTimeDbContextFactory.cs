using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EchoLifestyle.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tooling (dotnet ef migrations / database update).
/// It builds a context without interceptors or DI, because at design time
/// there is no request, no signed-in user and nothing to audit.
///
/// Override the connection with the ECHO_CONNECTION environment variable when
/// generating migrations against a different server.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EchoDbContext>
{
    private const string DefaultConnection =
        "Server=localhost;Database=EchoLifestyle;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public EchoDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ECHO_CONNECTION")
            ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<EchoDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", EchoDbContext.AdminSchema))
            .Options;

        return new EchoDbContext(options);
    }
}
