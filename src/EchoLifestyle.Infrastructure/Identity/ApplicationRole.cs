using Microsoft.AspNetCore.Identity;

namespace EchoLifestyle.Infrastructure.Identity;

/// <summary>
/// A named bundle of permissions. Permissions are stored as role claims of
/// type <c>permission</c>; nothing in the system authorizes on the role name
/// itself, so roles can be renamed or reshaped freely.
/// </summary>
public class ApplicationRole : IdentityRole<long>
{
    public string? Description { get; set; }

    /// <summary>
    /// System roles are seeded and cannot be deleted through the UI - removing
    /// Owner, for instance, would leave the business unable to administer itself.
    /// Their permissions can still be edited.
    /// </summary>
    public bool IsSystemRole { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public long? CreatedByUserId { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public long? ModifiedByUserId { get; set; }
}
