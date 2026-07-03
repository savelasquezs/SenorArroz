using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public class ColombiaTimeHelperTests
{
    [Fact]
    public void GetColombiaCalendarDateRangeUtc_same_day_includes_full_end_of_day()
    {
        var day = new DateTime(2026, 7, 3);

        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(day, day);

        Assert.Equal(new DateTime(2026, 7, 3, 5, 0, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 7, 4, 4, 59, 59, 999, DateTimeKind.Utc).AddTicks(9999), toUtc);
        Assert.True(toUtc > fromUtc);
    }
}
