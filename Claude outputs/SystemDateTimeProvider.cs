using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Time;

namespace EchoLifestyle.Infrastructure.Common;

/// <summary>
/// System clock. The only thing this adds is "now" - all conversion rules live
/// in <see cref="BusinessCalendar"/> so they can be tested without a clock.
/// </summary>
public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTimeOffset BusinessNow => BusinessCalendar.ToBusinessTime(DateTime.UtcNow);

    public DateOnly ToBusinessDate(DateTime utc) => BusinessCalendar.ToBusinessDate(utc);

    public DateTimeOffset ToBusinessTime(DateTime utc) => BusinessCalendar.ToBusinessTime(utc);
}
