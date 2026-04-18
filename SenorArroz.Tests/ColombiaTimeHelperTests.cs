using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public class ColombiaTimeHelperTests
{
    [Fact]
    public void GetPrepareAtSqlTruncDayUtc_returns_utc_kind_for_npgsql()
    {
        var utc = new DateTime(2026, 4, 14, 18, 30, 0, DateTimeKind.Utc);
        var bucket = ColombiaTimeHelper.GetPrepareAtSqlTruncDayUtc(utc);
        Assert.Equal(DateTimeKind.Utc, bucket.Kind);
        Assert.Equal(0, bucket.Hour);
        Assert.Equal(0, bucket.Minute);
        Assert.Equal(0, bucket.Second);
        Assert.Equal(2026, bucket.Year);
        Assert.Equal(4, bucket.Month);
        Assert.Equal(14, bucket.Day);
    }

    [Fact]
    public void GetPrepareAtSqlTruncDayUtc_matches_colombia_calendar_day()
    {
        var utc = new DateTime(2026, 4, 14, 18, 30, 0, DateTimeKind.Utc);
        var colombiaDateOnly = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(utc);
        var bucket = ColombiaTimeHelper.GetPrepareAtSqlTruncDayUtc(utc);
        Assert.Equal(colombiaDateOnly.Year, bucket.Year);
        Assert.Equal(colombiaDateOnly.Month, bucket.Month);
        Assert.Equal(colombiaDateOnly.Day, bucket.Day);
    }

    [Fact]
    public void GetPrepareAtSqlTruncDayUtc_near_colombia_midnight_matches_bogota_date()
    {
        // 2026-04-15 04:30 UTC = 2026-04-14 23:30 en Bogotá (UTC-5)
        var utc = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var colombiaDateOnly = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(utc);
        var bucket = ColombiaTimeHelper.GetPrepareAtSqlTruncDayUtc(utc);
        Assert.Equal(colombiaDateOnly.Year, bucket.Year);
        Assert.Equal(colombiaDateOnly.Month, bucket.Month);
        Assert.Equal(colombiaDateOnly.Day, bucket.Day);
        Assert.Equal(14, colombiaDateOnly.Day);
    }

    [Fact]
    public void IsSameColombiaCalendarDay_true_when_utc_dates_differ_but_bogota_day_matches()
    {
        var a = new DateTime(2026, 4, 15, 3, 0, 0, DateTimeKind.Utc);
        var b = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        Assert.True(ColombiaTimeHelper.IsSameColombiaCalendarDay(a, b));
    }

    [Fact]
    public void IsSameColombiaCalendarDay_false_across_bogota_midnight()
    {
        var lateNightCo = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var nextUtcMorning = new DateTime(2026, 4, 15, 6, 0, 0, DateTimeKind.Utc);
        Assert.False(ColombiaTimeHelper.IsSameColombiaCalendarDay(lateNightCo, nextUtcMorning));
    }

    [Fact]
    public void IsColombiaTodayFromUtc_true_when_utc_is_next_calendar_day_but_still_today_in_bogota()
    {
        var utcNow = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var createdEarlierSameCoDay = new DateTime(2026, 4, 14, 20, 0, 0, DateTimeKind.Utc);
        Assert.True(ColombiaTimeHelper.IsColombiaTodayFromUtc(createdEarlierSameCoDay, utcNow));
    }

    [Fact]
    public void IsColombiaTodayFromUtc_false_for_previous_bogota_day()
    {
        var utcNow = new DateTime(2026, 4, 15, 4, 30, 0, DateTimeKind.Utc);
        var previousCoDay = new DateTime(2026, 4, 13, 10, 0, 0, DateTimeKind.Utc);
        Assert.False(ColombiaTimeHelper.IsColombiaTodayFromUtc(previousCoDay, utcNow));
    }
}
