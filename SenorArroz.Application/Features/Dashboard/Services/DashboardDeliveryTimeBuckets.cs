using System.Globalization;

namespace SenorArroz.Application.Features.Dashboard.Services;

/// <summary>
/// Misma granularidad temporal que la evolución de entregas (hora / día / semana / mes).
/// </summary>
public sealed class DashboardDeliveryTimeBuckets
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    private readonly Func<DateTime, int> _indexOf;

    private DashboardDeliveryTimeBuckets(IReadOnlyList<string> labels, Func<DateTime, int> indexOf)
    {
        Labels = labels;
        _indexOf = indexOf;
    }

    public IReadOnlyList<string> Labels { get; }

    public int Count => Labels.Count;

    /// <summary>Índice en [0, Count) o -1 si no encaja en el rango construido.</summary>
    public int GetBucketIndex(DateTime utcInstant) => _indexOf(utcInstant);

    public static DashboardDeliveryTimeBuckets Create(DateTime fromUtc, DateTime toUtc)
    {
        var dayCount = InclusiveUtcDayCount(fromUtc, toUtc);

        if (dayCount <= 1)
        {
            var labels = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
            return new DashboardDeliveryTimeBuckets(labels, dt =>
            {
                var h = dt.Hour;
                return h is >= 0 and < 24 ? h : -1;
            });
        }

        if (dayCount <= 45)
        {
            var labels = new List<string>();
            var dayKeys = new List<DateTime>();
            for (var d = fromUtc.Date; d <= toUtc.Date; d = d.AddDays(1))
            {
                labels.Add(d.ToString("ddd, d MMM", EsCo));
                dayKeys.Add(d);
            }

            return new DashboardDeliveryTimeBuckets(labels, dt =>
            {
                var dd = dt.Date;
                for (var i = 0; i < dayKeys.Count; i++)
                {
                    if (dayKeys[i] == dd)
                        return i;
                }

                return -1;
            });
        }

        if (dayCount <= 120)
        {
            var labels = new List<string>();
            var weekStarts = new List<DateTime>();
            var cur = StartOfWeekUtc(fromUtc.Date);
            var end = toUtc.Date;
            while (cur <= end)
            {
                labels.Add($"Sem. {cur.ToString("dd MMM", EsCo)}");
                weekStarts.Add(cur);
                cur = cur.AddDays(7);
            }

            return new DashboardDeliveryTimeBuckets(labels, dt =>
            {
                var ws = StartOfWeekUtc(dt.Date);
                for (var i = 0; i < weekStarts.Count; i++)
                {
                    if (weekStarts[i] == ws)
                        return i;
                }

                return -1;
            });
        }

        {
            var labels = new List<string>();
            var monthKeys = new List<DateTime>();
            for (var m = new DateTime(fromUtc.Year, fromUtc.Month, 1);
                 m <= toUtc.Date;
                 m = m.AddMonths(1))
            {
                labels.Add(m.ToString("MMM yyyy", EsCo));
                monthKeys.Add(m);
            }

            return new DashboardDeliveryTimeBuckets(labels, dt =>
            {
                var key = new DateTime(dt.Year, dt.Month, 1);
                for (var i = 0; i < monthKeys.Count; i++)
                {
                    if (monthKeys[i] == key)
                        return i;
                }

                return -1;
            });
        }
    }

    private static DateTime StartOfWeekUtc(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }

    private static int InclusiveUtcDayCount(DateTime fromUtc, DateTime toUtc)
    {
        var f = fromUtc.Date;
        var t = toUtc.Date;
        return (int)(t - f).TotalDays + 1;
    }
}
