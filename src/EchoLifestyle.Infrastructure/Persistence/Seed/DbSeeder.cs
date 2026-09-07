using System.Security.Claims;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EchoLifestyle.Infrastructure.Persistence.Seed;

/// <summary>
/// Brings the database to a usable baseline: roles with their permissions, the
/// company, its first branch and warehouse, and the owner accounts.
///
/// Idempotent by design - it runs on every startup and only fills in what is
/// missing, so a newly added permission reaches the Owner role without anyone
/// remembering to re-run anything.
/// </summary>
public class DbSeeder
{
    private readonly EchoDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(
        EchoDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger<DbSeeder> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync(
        SeedOptions options,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        var branchId = await SeedOrganisationAsync(options, cancellationToken);
        await SeedCatalogBaselineAsync(cancellationToken);
        await SeedGeographyAsync(cancellationToken);
        await SeedExpenseCategoriesAsync(cancellationToken);
        await SeedOwnersAsync(options, branchId, isDevelopment);

        // After the owners, because a partner is seeded from the account that
        // already exists rather than from a name written twice.
        await SeedPartnersAsync(cancellationToken);
    }

    /// <summary>
    /// The categories money gets spent in.
    ///
    /// Idempotent by name and marked IsSystem, which is what stops a later run
    /// recreating a category somebody deliberately deactivated. Descriptions and
    /// the cost-of-sale flag are refreshed on every run so a correction here
    /// reaches an existing database; the name and the active flag are not,
    /// because those belong to whoever is using the system.
    /// </summary>
    private async Task SeedExpenseCategoriesAsync(CancellationToken cancellationToken)
    {
        var order = 0;
        var added = 0;

        foreach (var seed in FinanceSeed.ExpenseCategories)
        {
            order++;

            var category = await _db.ExpenseCategories
                .FirstOrDefaultAsync(c => c.Name == seed.Name, cancellationToken);

            if (category is null)
            {
                _db.ExpenseCategories.Add(new ExpenseCategory
                {
                    Name = seed.Name,
                    Description = seed.Description,
                    IsCostOfSale = seed.CostOfSale,
                    IsSystem = true,
                    IsActive = true,
                    DisplayOrder = order,
                });

                added++;
                continue;
            }

            category.Description = seed.Description;
            category.IsCostOfSale = seed.CostOfSale;
            category.IsSystem = true;
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (added > 0)
        {
            _logger.LogInformation("Seeded {Count} expense categories", added);
        }
    }

    /// <summary>
    /// A partner record for every owner account.
    ///
    /// Seeded from the accounts rather than from a hard-coded pair of names, so
    /// the capital screen is usable from the first login without anybody having
    /// been named in configuration twice. Linked by user id, so renaming an
    /// account does not create a second partner - and the partner record stays
    /// when the account eventually does not.
    /// </summary>
    private async Task SeedPartnersAsync(CancellationToken cancellationToken)
    {
        var owners = await _userManager.GetUsersInRoleAsync(Roles.Owner);

        if (owners.Count == 0)
        {
            return;
        }

        var order = 0;

        foreach (var owner in owners.OrderBy(o => o.UserName ?? string.Empty, StringComparer.Ordinal))
        {
            order++;

            var exists = await _db.Partners
                .AnyAsync(p => p.UserId == owner.Id, cancellationToken);

            if (exists)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(owner.FullName)
                ? owner.UserName ?? $"Partner {order}"
                : owner.FullName;

            // A partner may already have been added by hand under this name
            // before the account existed. Link the two rather than creating a
            // second capital account for the same person.
            var byName = await _db.Partners
                .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);

            if (byName is not null)
            {
                if (byName.UserId is null)
                {
                    byName.UserId = owner.Id;
                }

                continue;
            }

            _db.Partners.Add(new Partner
            {
                Name = name,
                UserId = owner.Id,
                IsActive = true,
                DisplayOrder = order,
                Notes = "Created from the owner account on first run.",
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded partner records from owner accounts");
        }
    }

    /// <summary>
    /// The eight divisions and sixty-four districts of Bangladesh.
    ///
    /// Reference data, not user data: nobody should be typing "Chattogram" into
    /// an address, because every courier prices by district code and a
    /// free-text spelling is worthless to them. Idempotent by name, so adding a
    /// missing district later is a one-line change to the seed rather than a
    /// migration.
    /// </summary>
    private async Task SeedGeographyAsync(CancellationToken cancellationToken)
    {
        var order = 0;

        foreach (var seed in GeographySeed.Divisions)
        {
            order++;

            var division = await _db.Divisions
                .FirstOrDefaultAsync(d => d.Name == seed.Name, cancellationToken);

            if (division is null)
            {
                division = new Division
                {
                    Name = seed.Name,
                    NameBn = seed.NameBn,
                    DisplayOrder = order,
                    IsActive = true,
                };

                _db.Divisions.Add(division);

                // Saved per division so the districts below have a real id to
                // hang off, without the seeder needing to know how EF orders
                // its inserts.
                await _db.SaveChangesAsync(cancellationToken);
            }

            var districtOrder = 0;

            foreach (var name in seed.Districts)
            {
                districtOrder++;

                if (await _db.Districts.AnyAsync(d => d.Name == name, cancellationToken))
                {
                    continue;
                }

                _db.Districts.Add(new District
                {
                    DivisionId = division.Id,
                    Name = name,
                    FormerName = GeographySeed.FormerNames.GetValueOrDefault(name),
                    IsInsideCity = GeographySeed.InsideCity.Contains(name),
                    DisplayOrder = districtOrder,
                    IsActive = true,
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The catalogue's two prerequisites: something to count products in, and
    /// somewhere for a price to go.
    ///
    /// Both are seeded rather than left to the user because the product form
    /// cannot save without them, and a first-run experience that fails on a
    /// foreign key is not one.
    /// </summary>
    private async Task SeedCatalogBaselineAsync(CancellationToken cancellationToken)
    {
        var units = new (string Code, string Name, bool Fractions)[]
        {
            ("PC", "Piece", false),
            ("PACK", "Pack", false),
            ("SET", "Set", false),
            ("ML", "Millilitre", true),
            ("G", "Gram", true),
        };

        foreach (var (code, name, fractions) in units)
        {
            if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Code == code, cancellationToken))
            {
                _db.UnitsOfMeasure.Add(new UnitOfMeasure
                {
                    Code = code,
                    Name = name,
                    AllowsFractions = fractions,
                    IsActive = true,
                });
            }
        }

        // Guarded on IsDefault rather than on the code, so that renaming the
        // retail list in the UI does not cause a second default to be created
        // on the next startup - which the unique index would reject anyway.
        if (!await _db.PriceLists.AnyAsync(p => p.IsDefault, cancellationToken))
        {
            _db.PriceLists.Add(new PriceList
            {
                Code = "RETAIL",
                Name = "Retail",
                Kind = PriceListKind.Retail,
                CurrencyCode = "BDT",
                IsDefault = true,
                IsActive = true,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the seed roles and synchronises their permission claims.
    /// Owner is always granted every permission that exists, so introducing a
    /// new permission never locks the owners out of their own system.
    /// </summary>
    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in Roles.All)
        {
            var role = await _roleManager.FindByNameAsync(roleName);

            if (role is null)
            {
                role = new ApplicationRole
                {
                    Name = roleName,
                    IsSystemRole = true,
                    Description = $"System role: {roleName}",
                };

                var created = await _roleManager.CreateAsync(role);
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not create role '{roleName}': {Describe(created)}");
                }

                _logger.LogInformation("Seeded role {RoleName}", roleName);
            }

            var desired = roleName == Roles.Owner
                ? Permissions.All.ToHashSet(StringComparer.Ordinal)
                : Roles.DefaultPermissions.TryGetValue(roleName, out var defaults)
                    ? defaults.ToHashSet(StringComparer.Ordinal)
                    : [];

            await SyncRolePermissionsAsync(role, desired, cancellationToken);
        }
    }

    /// <summary>
    /// Adds permissions the role should have and, for the Owner role, nothing
    /// is ever removed automatically. For other roles only permissions that no
    /// longer exist at all are pruned - deliberate grants made in the UI are
    /// left alone.
    /// </summary>
    private async Task SyncRolePermissionsAsync(
        ApplicationRole role,
        IReadOnlySet<string> desired,
        CancellationToken cancellationToken)
    {
        var existing = await _roleManager.GetClaimsAsync(role);
        var existingPermissions = existing
            .Where(c => c.Type == Permissions.ClaimType)
            .ToList();

        var existingValues = existingPermissions
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in desired.Where(p => !existingValues.Contains(p)))
        {
            var result = await _roleManager.AddClaimAsync(
                role, new Claim(Permissions.ClaimType, permission));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not grant '{permission}' to role '{role.Name}': {Describe(result)}");
            }
        }

        // Remove claims that reference permissions which no longer exist in code.
        foreach (var stale in existingPermissions.Where(c => !Permissions.Exists(c.Value)))
        {
            await _roleManager.RemoveClaimAsync(role, stale);
            _logger.LogWarning(
                "Removed obsolete permission {Permission} from role {RoleName}",
                stale.Value,
                role.Name);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates the company, its first branch and warehouse. Returns the branch id.</summary>
    private async Task<long> SeedOrganisationAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            company = new Company
            {
                Name = options.CompanyName,
                LegalName = options.CompanyName,
                CountryCode = "BD",
                BaseCurrencyCode = "BDT",
                BusinessTimeZoneId = "Asia/Dhaka",
            };

            _db.Companies.Add(company);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded company {CompanyName}", company.Name);
        }

        var branch = await _db.Branches
            .FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == options.BranchCode, cancellationToken);

        if (branch is null)
        {
            branch = new Branch
            {
                CompanyId = company.Id,
                Code = options.BranchCode,
                Name = options.BranchName,
                Type = BranchType.Online,
                IsActive = true,

                // Deliberate default: stock may not go negative. Changing this
                // is a policy decision, and the acting user still needs
                // Inventory.Stock.AllowNegative.
                AllowNegativeStock = false,
            };

            _db.Branches.Add(branch);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded branch {BranchCode}", branch.Code);
        }

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.CompanyId == company.Id && w.Code == options.WarehouseCode, cancellationToken);

        if (warehouse is null)
        {
            warehouse = new Warehouse
            {
                CompanyId = company.Id,
                Code = options.WarehouseCode,
                Name = options.WarehouseName,
                Kind = WarehouseKind.Sellable,
                IsActive = true,
            };

            _db.Warehouses.Add(warehouse);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded warehouse {WarehouseCode}", warehouse.Code);
        }

        var link = await _db.BranchWarehouses
            .FirstOrDefaultAsync(bw => bw.BranchId == branch.Id && bw.WarehouseId == warehouse.Id, cancellationToken);

        if (link is null)
        {
            _db.BranchWarehouses.Add(new BranchWarehouse
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                IsPrimary = true,
                Priority = 1,
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        return branch.Id;
    }

    private async Task SeedOwnersAsync(SeedOptions options, long branchId, bool isDevelopment)
    {
        foreach (var owner in options.Owners)
        {
            if (string.IsNullOrWhiteSpace(owner.UserName))
            {
                continue;
            }

            var user = await _userManager.FindByNameAsync(owner.UserName);

            if (user is not null)
            {
                await EnsureOwnerRoleAsync(user);
                await EnsureBranchAssignmentAsync(user.Id, branchId);
                continue;
            }

            var password = owner.Password;

            if (string.IsNullOrWhiteSpace(password))
            {
                if (!isDevelopment)
                {
                    _logger.LogError(
                        "No seed password configured for owner {UserName}. The account was NOT created. " +
                        "Set Seed:Owners:<n>:Password in configuration or user-secrets.",
                        owner.UserName);
                    continue;
                }

                password = GenerateTemporaryPassword();
                _logger.LogWarning(
                    "Created owner {UserName} with a generated development password: {Password} - change it immediately.",
                    owner.UserName,
                    password);
            }

            user = new ApplicationUser
            {
                UserName = owner.UserName,
                Email = owner.Email,
                EmailConfirmed = true,
                FullName = owner.FullName,

                // Staff, fixed at creation. See UserType for why this is not a role.
                UserType = UserType.Staff,
                IsActive = true,
                Notes = "Seeded owner account",
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create owner '{owner.UserName}': {Describe(result)}");
            }

            await EnsureOwnerRoleAsync(user);
            await EnsureBranchAssignmentAsync(user.Id, branchId);

            _logger.LogInformation("Seeded owner account {UserName}", owner.UserName);
        }
    }

    private async Task EnsureOwnerRoleAsync(ApplicationUser user)
    {
        if (!await _userManager.IsInRoleAsync(user, Roles.Owner))
        {
            var result = await _userManager.AddToRoleAsync(user, Roles.Owner);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not add '{user.UserName}' to the Owner role: {Describe(result)}");
            }
        }
    }

    private async Task EnsureBranchAssignmentAsync(long userId, long branchId)
    {
        var exists = await _db.UserBranches
            .AnyAsync(ub => ub.UserId == userId && ub.BranchId == branchId);

        if (exists)
        {
            return;
        }

        var hasDefault = await _db.UserBranches.AnyAsync(ub => ub.UserId == userId && ub.IsDefault);

        _db.UserBranches.Add(new UserBranch
        {
            UserId = userId,
            BranchId = branchId,
            IsDefault = !hasDefault,
        });

        await _db.SaveChangesAsync();
    }

    private static string GenerateTemporaryPassword()
    {
        // Meets the configured Identity policy without being guessable.
        var random = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("=", string.Empty, StringComparison.Ordinal)
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal);

        return $"Echo!{random}9";
    }

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
