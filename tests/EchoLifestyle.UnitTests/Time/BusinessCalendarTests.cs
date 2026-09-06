using EchoLifestyle.Application.Common.Time;

namespace EchoLifestyle.UnitTests.Time;

public class BusinessCalendarTests
{
    [Fact]
    public void Business_zone_is_six_hours_ahead_of_utc()
    {
        var offset = BusinessCalendar.BusinessZone.GetUtcOffset(new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(6), offset);
    }

    [Fact]
    public void Bangladesh_does_not_observe_daylight_saving()
    {
        // Checked at both solstices: if this ever changes, every stored
        // timestamp conversion and every daily total shifts by an hour.
        var winter = BusinessCalendar.BusinessZone.GetUtcOffset(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        var summer = BusinessCalendar.BusinessZone.GetUtcOffset(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(winter, summer);
    }

    [Theory]
    // 20:00 UTC is already 02:00 the next day in Dhaka - a late-evening sale
    // belongs to tomorrow's business date, not today's.
    [InlineData("2026-01-01T20:00:00Z", "2026-01-02")]
    // 18:00 UTC is exactly midnight Dhaka: the first instant of the next day.
    [InlineData("2026-01-01T18:00:00Z", "2026-01-02")]
    // One second earlier is still the previous business day.
    [InlineData("2026-01-01T17:59:59Z", "2026-01-01")]
    [InlineData("2026-03-10T06:30:00Z", "2026-03-10")]
    public void Business_date_is_derived_in_Dhaka_not_utc(string utcText, string expectedDate)
    {
        var utc = DateTime.Parse(utcText, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateOnly.Parse(expectedDate);

        Assert.Equal(expected, BusinessCalendar.ToBusinessDate(utc));
    }

    [Fact]
    public void Business_day_range_is_half_open_and_covers_exactly_24_hours()
    {
        var date = new DateOnly(2026, 5, 20);
        var (startUtc, endUtc) = BusinessCalendar.BusinessDayRangeUtc(date);

        // 00:00 Dhaka on the 20th is 18:00 UTC on the 19th.
        Assert.Equal(new DateTime(2026, 5, 19, 18, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 5, 20, 18, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.Equal(TimeSpan.FromHours(24), endUtc - startUtc);
    }

    [Fact]
    public void Consecutive_business_days_meet_without_gap_or_overlap()
    {
        // The end of one day must be the exact start of the next, or rows fall
        // between two reports - or get counted in both.
        var first = BusinessCalendar.BusinessDayRangeUtc(new DateOnly(2026, 5, 20));
        var second = BusinessCalendar.BusinessDayRangeUtc(new DateOnly(2026, 5, 21));

        Assert.Equal(first.EndUtc, second.StartUtc);
    }

    [Fact]
    public void Every_instant_in_a_day_range_maps_back_to_that_business_date()
    {
        var date = new DateOnly(2026, 5, 20);
        var (startUtc, endUtc) = BusinessCalendar.BusinessDayRangeUtc(date);

        Assert.Equal(date, BusinessCalendar.ToBusinessDate(startUtc));
        Assert.Equal(date, BusinessCalendar.ToBusinessDate(endUtc.AddTicks(-1)));

        // The exclusive upper bound belongs to the following day.
        Assert.Equal(date.AddDays(1), BusinessCalendar.ToBusinessDate(endUtc));
    }

    [Fact]
    public void Unspecified_kind_is_treated_as_utc_rather_than_server_local_time()
    {
        // Values read back from SQL Server arrive as Unspecified. Treating them
        // as the server's local time would silently shift every timestamp on a
        // machine that is not itself on UTC.
        var unspecified = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(BusinessCalendar.ToBusinessDate(utc), BusinessCalendar.ToBusinessDate(unspecified));
    }
}
