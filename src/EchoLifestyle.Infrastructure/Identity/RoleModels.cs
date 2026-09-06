namespace EchoLifestyle.Infrastructure.Identity;

public class RoleListItem
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsSystemRole { get; init; }

    /// <summary>Owner is granted every permission implicitly, so its count is the full set.</summary>
    public int PermissionCount { get; init; }

    public int UserCount { get; init; }

    /// <summary>Owner's permissions are not editable - it always holds everything.</summary>
    public bool PermissionsAreFixed { get; init; }
}

public class RoleDetail
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsSystemRole { get; init; }

    public bool PermissionsAreFixed { get; init; }

    public int UserCount { get; init; }

    public IReadOnlyCollection<string> Permissions { get; init; } = [];
}

public class SaveRoleRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<string> Permissions { get; set; } = [];
}
