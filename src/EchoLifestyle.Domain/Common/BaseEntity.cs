namespace EchoLifestyle.Domain.Common;

/// <summary>
/// Base for all persisted entities.
/// Keys are bigint identities per the approved database conventions.
/// </summary>
public abstract class BaseEntity
{
    public long Id { get; set; }

    /// <summary>
    /// SQL Server rowversion, used for optimistic concurrency.
    /// Never set by application code.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
