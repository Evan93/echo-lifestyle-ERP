namespace EchoLifestyle.Domain.Common;

/// <summary>
/// Applied only to master data that may legitimately be retired.
/// Never applied to posted financial or stock documents - those are
/// reversed or cancelled, never deleted.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAtUtc { get; set; }

    long? DeletedByUserId { get; set; }
}
