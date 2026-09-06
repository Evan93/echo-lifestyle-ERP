namespace EchoLifestyle.Infrastructure.Identity;

public class StaffUserListItem
{
    public long Id { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string? Email { get; init; }

    public bool IsActive { get; init; }

    /// <summary>True while Identity is holding the account after failed sign-ins.</summary>
    public bool IsLockedOut { get; init; }

    public DateTime? LastLoginAtUtc { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public int BranchCount { get; init; }
}

public class StaffUserDetail
{
    public long Id { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string? Email { get; init; }

    public string? Notes { get; init; }

    public bool IsActive { get; init; }

    public bool IsLockedOut { get; init; }

    public DateTime? LockoutEndUtc { get; init; }

    public DateTime? LastLoginAtUtc { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<long> BranchIds { get; init; } = [];

    public long? DefaultBranchId { get; init; }
}

public class SaveStaffUserRequest
{
    /// <summary>Only used when creating. A username is permanent once issued.</summary>
    public string UserName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Required when creating; ignored on edit - use the reset action instead.</summary>
    public string? Password { get; set; }

    public List<string> Roles { get; set; } = [];

    public List<long> BranchIds { get; set; } = [];

    public long? DefaultBranchId { get; set; }
}
