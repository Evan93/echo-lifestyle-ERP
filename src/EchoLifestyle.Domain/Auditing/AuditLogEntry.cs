using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Auditing;

/// <summary>
/// Append-only record of security- and business-sensitive actions.
///
/// This is deliberately not a blanket row-change log: indiscriminate table
/// auditing is expensive and produces noise nobody reads. Entries are written
/// explicitly for actions that matter - role and permission changes, user
/// creation and lockout, price and cost changes, approvals, period close,
/// and any override of a business rule.
/// </summary>
public class AuditLogEntry : BaseEntity
{
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Identity user id that performed the action; null for system actions.</summary>
    public long? UserId { get; set; }

    /// <summary>Denormalised on purpose: the log must stay readable if the user is renamed or removed.</summary>
    public string? UserName { get; set; }

    /// <summary>Dotted action key, e.g. "Security.Role.PermissionsChanged".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Logical entity affected, e.g. "ApplicationUser".</summary>
    public string? EntityName { get; set; }

    /// <summary>Key of the affected entity, as text so any key type is supported.</summary>
    public string? EntityId { get; set; }

    /// <summary>Branch the action was performed in scope of, when applicable.</summary>
    public long? BranchId { get; set; }

    /// <summary>Human-readable summary shown in the audit screen.</summary>
    public string? Summary { get; set; }

    /// <summary>Structured detail (JSON) - before/after values, reason, override justification.</summary>
    public string? DetailJson { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Correlates the entry with the request's trace id in the logs.</summary>
    public string? TraceId { get; set; }
}
