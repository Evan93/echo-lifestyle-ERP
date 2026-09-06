namespace EchoLifestyle.Application.Common.Interfaces;

/// <summary>
/// Writes entries to the audit trail for security- and business-sensitive
/// actions. Called explicitly by use cases rather than blanket-logging every
/// row change, so the trail stays small enough that people actually read it.
/// </summary>
public interface IAuditLogger
{
    /// <param name="action">Dotted action key, e.g. "Security.Role.PermissionsChanged".</param>
    /// <param name="entityName">Logical entity affected, e.g. "ApplicationUser".</param>
    /// <param name="entityId">Key of the affected entity, as text.</param>
    /// <param name="summary">Human-readable one-line description.</param>
    /// <param name="detail">Anything structured worth keeping - before/after, reason, override justification.</param>
    /// <param name="branchId">Branch scope of the action, when applicable.</param>
    Task LogAsync(
        string action,
        string? entityName = null,
        string? entityId = null,
        string? summary = null,
        object? detail = null,
        long? branchId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Well-known audit action keys.</summary>
public static class AuditActions
{
    public const string UserCreated = "Security.User.Created";
    public const string UserLocked = "Security.User.Locked";
    public const string UserUnlocked = "Security.User.Unlocked";
    public const string UserRolesChanged = "Security.User.RolesChanged";
    public const string UserPasswordReset = "Security.User.PasswordReset";
    public const string RoleCreated = "Security.Role.Created";
    public const string RoleDeleted = "Security.Role.Deleted";
    public const string RolePermissionsChanged = "Security.Role.PermissionsChanged";
    public const string LoginSucceeded = "Security.Login.Succeeded";
    public const string LoginFailed = "Security.Login.Failed";
    public const string LoginLockedOut = "Security.Login.LockedOut";
    public const string CompanyUpdated = "Administration.Company.Updated";
    public const string BranchCreated = "Administration.Branch.Created";
    public const string BranchUpdated = "Administration.Branch.Updated";
    public const string WarehouseCreated = "Administration.Warehouse.Created";
    public const string WarehouseUpdated = "Administration.Warehouse.Updated";
}
