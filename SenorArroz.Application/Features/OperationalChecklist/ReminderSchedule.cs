using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.OperationalChecklist;

public static class ReminderSchedule
{
    public static bool Applies(OperationalReminder reminder, DateOnly day)
    {
        if (!reminder.IsActive || day.DayOfWeek == DayOfWeek.Wednesday) return false;
        if (reminder.ShiftMondayHoliday)
            return day.DayOfWeek == DayOfWeek.Monday && !IsColombianHoliday(day)
                || day.DayOfWeek == DayOfWeek.Tuesday && IsColombianHoliday(day.AddDays(-1));
        return reminder.DaysOfWeek.Contains((int)day.DayOfWeek);
    }

    // National holidays only; no network dependency for showing the daily checklist.
    public static bool IsColombianHoliday(DateOnly day)
    {
        var y = day.Year;
        var fixedDates = new[] { new DateOnly(y, 1, 1), new(y, 5, 1), new(y, 7, 20), new(y, 8, 7), new(y, 12, 8), new(y, 12, 25) };
        if (fixedDates.Contains(day)) return true;
        var mondayDates = new[] { new DateOnly(y, 1, 6), new(y, 3, 19), new(y, 6, 29), new(y, 8, 15), new(y, 10, 12), new(y, 11, 1), new(y, 11, 11) };
        if (mondayDates.Select(NextMonday).Contains(day)) return true;
        var easter = Easter(y);
        return day == easter.AddDays(-3) || day == easter.AddDays(-2)
            || day == NextMonday(easter.AddDays(39)) || day == NextMonday(easter.AddDays(60))
            || day == NextMonday(easter.AddDays(68));
    }

    private static DateOnly NextMonday(DateOnly d) => d.AddDays((8 - (int)d.DayOfWeek) % 7);
    private static DateOnly Easter(int year)
    {
        var a = year % 19; var b = year / 100; var c = year % 100;
        var d = b / 4; var e = b % 4; var f = (b + 8) / 25; var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30; var i = c / 4; var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7; var m = (a + 11 * h + 22 * l) / 451;
        var n = h + l - 7 * m + 114;
        return new DateOnly(year, n / 31, n % 31 + 1);
    }
}
