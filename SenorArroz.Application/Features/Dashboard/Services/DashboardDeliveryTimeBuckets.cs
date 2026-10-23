using System.Globalization;
using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Application.Features.Dashboard.Services;

public sealed class DashboardDeliveryTimeBuckets
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");
    private readonly Func<DateTime, int> _indexOf;
    private readonly DateTime _fromUtc;
    private readonly DateTime _toUtc;
    private readonly bool _excludeWednesdays;

    private DashboardDeliveryTimeBuckets(IReadOnlyList<string> labels, Func<DateTime, int> indexOf,
        DateTime fromUtc, DateTime toUtc, bool excludeWednesdays)
    {
        Labels = labels;
        _indexOf = indexOf;
        _fromUtc = fromUtc;
        _toUtc = toUtc;
        _excludeWednesdays = excludeWednesdays;
    }

    public IReadOnlyList<string> Labels { get; }
    public int Count => Labels.Count;

    public int GetBucketIndex(DateTime utcInstant) => utcInstant < _fromUtc || utcInstant > _toUtc
        || !DeliveryPerformanceAnalytics.IncludesDay(utcInstant, _excludeWednesdays)
            ? -1 : _indexOf(ColombiaTimeHelper.GetNowInColombiaFromUtc(utcInstant));

    public static DashboardDeliveryTimeBuckets Create(DateTime fromUtc, DateTime toUtc, bool excludeWednesdays = false)
    {
        var from = ColombiaTimeHelper.GetNowInColombiaFromUtc(fromUtc).Date;
        var to = ColombiaTimeHelper.GetNowInColombiaFromUtc(toUtc).Date;
        var days = (int)(to - from).TotalDays + 1;
        if (days <= 1)
        {
            var excluded = excludeWednesdays && from.DayOfWeek == DayOfWeek.Wednesday;
            return new(excluded ? [] : Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList(),
                date => excluded ? -1 : date.Hour, fromUtc, toUtc, excludeWednesdays);
        }
        var keys = new List<DateTime>();
        Func<DateTime, DateTime> keyOf;
        string format;
        if (days <= 45)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
                if (!excludeWednesdays || date.DayOfWeek != DayOfWeek.Wednesday) keys.Add(date);
            keyOf = date => date.Date;
            format = "ddd, d MMM";
        }
        else if (days <= 120)
        {
            for (var date = StartOfWeek(from); date <= to; date = date.AddDays(7)) keys.Add(date);
            keyOf = StartOfWeek;
            format = "'Sem.' dd MMM";
        }
        else
        {
            for (var date = new DateTime(from.Year, from.Month, 1); date <= to; date = date.AddMonths(1)) keys.Add(date);
            keyOf = date => new DateTime(date.Year, date.Month, 1);
            format = "MMM yyyy";
        }
        var indexes = keys.Select((date, index) => (date, index)).ToDictionary(x => x.date, x => x.index);
        return new(keys.Select(date => date.ToString(format, EsCo)).ToList(),
            date => indexes.GetValueOrDefault(keyOf(date), -1), fromUtc, toUtc, excludeWednesdays);
    }

    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
}
