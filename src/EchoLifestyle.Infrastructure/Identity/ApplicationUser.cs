using EchoLifestyle.Domain.Security;
using Microsoft.AspNetCore.Identity;

namespace EchoLifestyle.Infrastructure.Identity;

/// <summary>
/// Application user. Both back-office staff and storefront customers live in
/// this table, separated by <see cref="UserType"/> - see the type's remarks for
/// why that separation is structural rather than role-based.
/// </summary>
public class ApplicationUser : IdentityUser<long>
{
    /// <summary>
    /// Staff or Customer. Set at creation and never changed afterwards; a
    /// customer who joins the company gets a new staff account rather than an
    /// upgrade, so no account can silently cross the boundary.
    /// </summary>
    public UserType UserType { get; set; } = UserType.Customer;

    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public long? CreatedByUserId { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public long? ModifiedByUserId { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Optional note shown in user administration, e.g. "co-owner".</summary>
    public string? Notes { get; set; }
}
