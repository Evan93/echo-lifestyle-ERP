using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Security;

/// <summary>
/// Assigns a staff user to a branch. Non-owner users only see and act on data
/// belonging to branches assigned here - this is the branch-level data scope.
///
/// Deliberately holds only the user id rather than a navigation to the Identity
/// user, so the domain stays free of any ASP.NET Identity dependency.
/// </summary>
public class UserBranch : AuditableEntity
{
    public long UserId { get; set; }

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>The branch selected by default when the user signs in.</summary>
    public bool IsDefault { get; set; }
}
