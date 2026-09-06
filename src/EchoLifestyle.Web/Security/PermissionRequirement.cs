using Microsoft.AspNetCore.Authorization;

namespace EchoLifestyle.Web.Security;

/// <summary>
/// Requires a single action-based permission, e.g. "Inventory.Transfer.Approve".
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
