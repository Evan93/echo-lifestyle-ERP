namespace EchoLifestyle.Infrastructure.Persistence.Seed;

/// <summary>
/// Seed data supplied by the host. Passwords are never hard-coded in source:
/// they come from configuration or user-secrets, and outside Development the
/// seeder refuses to invent one.
/// </summary>
public class SeedOptions
{
    public const string SectionName = "Seed";

    public string CompanyName { get; set; } = "Echo Lifestyle";

    public string BranchCode { get; set; } = "ONLINE";

    public string BranchName { get; set; } = "Online Store";

    public string WarehouseCode { get; set; } = "MAIN";

    public string WarehouseName { get; set; } = "Main Warehouse";

    public List<OwnerSeed> Owners { get; set; } = [];
}

public class OwnerSeed
{
    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Required outside Development. In Development a temporary password is
    /// generated and written to the startup log so it is never committed.
    /// </summary>
    public string? Password { get; set; }
}
