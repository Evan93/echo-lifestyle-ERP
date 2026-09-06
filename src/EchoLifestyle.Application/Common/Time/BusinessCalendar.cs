namespace EchoLifestyle.Application.Common.Time;

/// <summary>
/// Conversion between stored UTC and the business day in Asia/Dhaka.
///
/// This is pure logic with no clock and no I/O, so the rule that decides which
/// day a sale belongs to can be tested directly. Getting it wrong is not a
/// cosmetic bug: an order placed at 03:00 Dhaka is 21:00 UTC the previous day,
/// so grouping reports by the UTC date would move revenue between days and
/// quietly break every daily total and target.
/// </summary>
public static class BusinessCalendar
{
    private const string IanaDhaka = "Asia/Dhaka";
    private const string WindowsDhaka = "Bangladesh Standard Time";

    /// <summary>Asia/Dhaka, resolved once. Bangladesh has observed UTC+6 with no DST since 2010.</summary>
    public static TimeZoneInfo BusinessZone { get; } = Resolve();

    public static DateTimeOffset ToBusinessTime(DateTime utc)
    {
        var instant = utc.Kind == DateTimeKind.Utc
            ? utc
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(instant), BusinessZone);
    }

    /// <summary>The business date a UTC instant falls on. Reports group by this.</summary>
    public static DateOnly ToBusinessDate(DateTime utc) =>
        DateOnly.FromDateTime(ToBusinessTime(utc).DateTime);

    /// <summary>
    /// The UTC half-open range [start, end) covering one business day.
    /// Queries filter with <c>&gt;= start &amp;&amp; &lt; end</c> so nothing is
    /// double-counted at the boundary and nothing falls between two days.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) BusinessDayRangeUtc(DateOnly businessDate)
    {
        var localStart = businessDate.ToDateTime(TimeOnly.MinValue);
        var localEnd = businessDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        return (ToUtc(localStart), ToUtc(localEnd));
    }

    private static DateTime ToUtc(DateTime unspecifiedLocal)
    {
        var local = DateTime.SpecifyKind(unspecifiedLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, BusinessZone);
    }

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaDhaka);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows without ICU still knows the zone by its own identifier.
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsDhaka);
        }
    }
}
