namespace EchoLifestyle.Domain.Common;

/// <summary>
/// Base for entities that carry creation/modification audit fields.
/// Timestamps are always stored in UTC; conversion to Asia/Dhaka happens
/// at the presentation and reporting boundary only.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Identity user id of the creator, or null for system/seed data.</summary>
    public long? CreatedByUserId { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    public long? ModifiedByUserId { get; set; }
}
