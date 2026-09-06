namespace EchoLifestyle.Application.Administration.Auditing;

public class AuditListItem
{
    public long Id { get; init; }

    /// <summary>Stored in UTC.</summary>
    public DateTime OccurredAtUtc { get; init; }

    /// <summary>Same instant rendered in Asia/Dhaka, which is what people recognise.</summary>
    public string OccurredAtLocal { get; init; } = string.Empty;

    public string? UserName { get; init; }

    public string Action { get; init; } = string.Empty;

    /// <summary>First dotted segment of the action, e.g. "Security".</summary>
    public string Module { get; init; } = string.Empty;

    public string? EntityName { get; init; }

    public string? EntityId { get; init; }

    public string? Summary { get; init; }

    public string? DetailJson { get; init; }

    public string? IpAddress { get; init; }

    public string? TraceId { get; init; }
}

/// <summary>Filters for the audit trail. All optional.</summary>
public class AuditFilter
{
    /// <summary>Business date in Asia/Dhaka, inclusive.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Business date in Asia/Dhaka, inclusive.</summary>
    public DateOnly? ToDate { get; set; }

    public string? Action { get; set; }

    public string? Module { get; set; }

    public string? UserName { get; set; }

    /// <summary>Free text across summary, action, entity and username.</summary>
    public string? Search { get; set; }
}
