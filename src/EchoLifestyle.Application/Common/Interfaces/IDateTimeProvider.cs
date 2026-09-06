namespace EchoLifestyle.Application.Common.Interfaces;

/// <summary>
/// All time access goes through this so business logic stays testable and
/// so the UTC-storage / Dhaka-presentation rule is applied in exactly one place.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Current instant in UTC. This is what gets persisted.</summary>
    DateTime UtcNow { get; }

    /// <summary>Current wall-clock time in the business time zone (Asia/Dhaka).</summary>
    DateTimeOffset BusinessNow { get; }

    /// <summary>
    /// The business date a UTC instant belongs to, in Asia/Dhaka.
    /// Reports group by this, never by the raw UTC date - otherwise sales made
    /// after 6am Dhaka would land on the wrong day.
    /// </summary>
    DateOnly ToBusinessDate(DateTime utc);

    /// <summary>Converts a UTC instant to business-zone local time for display.</summary>
    DateTimeOffset ToBusinessTime(DateTime utc);
}
