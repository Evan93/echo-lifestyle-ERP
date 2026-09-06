using System.Security.Claims;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EchoLifestyle.Web.Security;

/// <summary>
/// Extends the standard principal with the claims this application authorizes on.
///
/// Role claims - which is how permissions are stored - are added by the base
/// factory. This adds the user type and the branches the user may act in.
/// </summary>
public class AppUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly EchoDbContext _db;

    public AppUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> options,
        EchoDbContext db)
        : base(userManager, roleManager, options)
    {
        _db = db;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(EchoClaimTypes.UserType, user.UserType.ToString()));
        identity.AddClaim(new Claim(EchoClaimTypes.FullName, user.FullName));

        var branches = await _db.UserBranches
            .AsNoTracking()
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => new { ub.BranchId, ub.IsDefault })
            .ToListAsync();

        foreach (var branch in branches)
        {
            identity.AddClaim(new Claim(
                EchoClaimTypes.Branch,
                branch.BranchId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var defaultBranch = branches.FirstOrDefault(b => b.IsDefault) ?? branches.FirstOrDefault();
        if (defaultBranch is not null)
        {
            identity.AddClaim(new Claim(
                EchoClaimTypes.DefaultBranch,
                defaultBranch.BranchId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return identity;
    }
}
