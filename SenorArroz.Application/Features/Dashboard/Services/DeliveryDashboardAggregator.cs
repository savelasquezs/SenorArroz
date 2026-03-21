using System.Globalization;
using System.Text.Json;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Dashboard.Services;

/// <summary>
/// Agrega pedidos de domicilio entregados en un rango para el dashboard.
/// </summary>
public static class DeliveryDashboardAggregator
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    public static DeliveryAggregatesDto Build(
        IReadOnlyList<Order> orders,
        DateTime fromUtc,
        DateTime toUtc)
    {
        if (orders.Count == 0)
        {
            return new DeliveryAggregatesDto(
                0, 0,
                new List<DeliverymanEfficiencyRowDto>(),
                new List<string>(),
                new List<int>(),
                new List<int>());
        }

        var prepList = new List<double>();
        var delList = new List<double>();

        foreach (var o in orders)
        {
            var times = ParseStatusTimes(o.StatusTimes);
            var taken = GetTime(times, "taken");
            var inPrep = GetTime(times, "inpreparation", "in_preparation");
            var ready = GetTime(times, "ready");
            var delivered = GetTime(times, "delivered");

            if (ready.HasValue && taken.HasValue)
                prepList.Add((ready.Value - taken.Value).TotalMinutes);
            else if (ready.HasValue && inPrep.HasValue)
                prepList.Add((ready.Value - inPrep.Value).TotalMinutes);

            if (delivered.HasValue && ready.HasValue)
                delList.Add((delivered.Value - ready.Value).TotalMinutes);
        }

        var avgPrep = prepList.Count > 0 ? Math.Round(prepList.Average(), 1) : 0d;
        var avgDel = delList.Count > 0 ? Math.Round(delList.Average(), 1) : 0d;

        var byDriver = orders
            .Where(o => o.DeliveryManId.HasValue && o.DeliveryMan != null)
            .GroupBy(o => o.DeliveryManId!.Value)
            .Select(g =>
            {
                var first = g.First();
                var dm = first.DeliveryMan!;
                var fees = g.Sum(x => x.DeliveryFee ?? 0);
                var dMins = new List<double>();
                foreach (var o in g)
                {
                    var times = ParseStatusTimes(o.StatusTimes);
                    var ready = GetTime(times, "ready");
                    var delivered = GetTime(times, "delivered");
                    if (delivered.HasValue && ready.HasValue)
                        dMins.Add((delivered.Value - ready.Value).TotalMinutes);
                }

                return new DeliverymanEfficiencyRowDto(
                    dm.Id,
                    first.BranchId,
                    dm.Name ?? $"#{dm.Id}",
                    g.Count(),
                    dMins.Count > 0 ? Math.Round(dMins.Average(), 1) : avgDel,
                    fees);
            })
            .OrderByDescending(x => x.DeliveredCount)
            .ToList();

        var (labels, counts, fees) = BuildEvolution(orders, fromUtc, toUtc);

        return new DeliveryAggregatesDto(avgPrep, avgDel, byDriver, labels, counts, fees);
    }

    private static Dictionary<string, DateTime> ParseStatusTimes(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
            return d != null
                ? new Dictionary<string, DateTime>(d, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static DateTime? GetTime(Dictionary<string, DateTime> d, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (d.TryGetValue(k, out var t))
                return t;
        }

        foreach (var kv in d)
        {
            foreach (var k in keys)
            {
                if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
        }

        return null;
    }

    private static (List<string> Labels, List<int> Counts, List<int> Fees) BuildEvolution(
        IReadOnlyList<Order> orders,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var dayCount = InclusiveUtcDayCount(fromUtc, toUtc);

        if (dayCount <= 1)
        {
            var hourBuckets = new int[24];
            var hourFees = new int[24];
            foreach (var o in orders)
            {
                var h = o.UpdatedAt.Hour;
                hourBuckets[h]++;
                hourFees[h] += o.DeliveryFee ?? 0;
            }

            var labels = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
            return (labels, hourBuckets.ToList(), hourFees.ToList());
        }

        if (dayCount <= 45)
        {
            var byDay = orders.GroupBy(o => o.UpdatedAt.Date).ToDictionary(g => g.Key, g => g.ToList());
            var labels = new List<string>();
            var counts = new List<int>();
            var feeTotals = new List<int>();
            for (var d = fromUtc.Date; d <= toUtc.Date; d = d.AddDays(1))
            {
                labels.Add(d.ToString("ddd, d MMM", EsCo));
                if (!byDay.TryGetValue(d, out var list))
                {
                    counts.Add(0);
                    feeTotals.Add(0);
                }
                else
                {
                    counts.Add(list.Count);
                    feeTotals.Add(list.Sum(x => x.DeliveryFee ?? 0));
                }
            }

            return (labels, counts, feeTotals);
        }

        if (dayCount <= 120)
        {
            // Semanas (lunes como inicio)
            var weekStarts = new Dictionary<DateTime, List<Order>>();
            foreach (var o in orders)
            {
                var ds = StartOfWeekUtc(o.UpdatedAt.Date);
                if (!weekStarts.TryGetValue(ds, out var l))
                {
                    l = new List<Order>();
                    weekStarts[ds] = l;
                }

                l.Add(o);
            }

            var labels = new List<string>();
            var counts = new List<int>();
            var feeTotals = new List<int>();
            var cur = StartOfWeekUtc(fromUtc.Date);
            var end = toUtc.Date;
            while (cur <= end)
            {
                labels.Add($"Sem. {cur.ToString("dd MMM", EsCo)}");
                if (!weekStarts.TryGetValue(cur, out var list))
                {
                    counts.Add(0);
                    feeTotals.Add(0);
                }
                else
                {
                    counts.Add(list.Count);
                    feeTotals.Add(list.Sum(x => x.DeliveryFee ?? 0));
                }

                cur = cur.AddDays(7);
            }

            return (labels, counts, feeTotals);
        }

        {
            var byMonth = orders.GroupBy(o => new DateTime(o.UpdatedAt.Year, o.UpdatedAt.Month, 1))
                .ToDictionary(g => g.Key, g => g.ToList());
            var labels = new List<string>();
            var counts = new List<int>();
            var feeTotals = new List<int>();
            for (var m = new DateTime(fromUtc.Year, fromUtc.Month, 1);
                 m <= toUtc.Date;
                 m = m.AddMonths(1))
            {
                labels.Add(m.ToString("MMM yyyy", EsCo));
                if (!byMonth.TryGetValue(m, out var list))
                {
                    counts.Add(0);
                    feeTotals.Add(0);
                }
                else
                {
                    counts.Add(list.Count);
                    feeTotals.Add(list.Sum(x => x.DeliveryFee ?? 0));
                }
            }

            return (labels, counts, feeTotals);
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

public record DeliverymanEfficiencyRowDto(
    int Id,
    int BranchId,
    string Name,
    int DeliveredCount,
    double AvgDeliveryMinutes,
    int DeliveryFeeTotal);

public record DeliveryAggregatesDto(
    double AvgPrepMinutes,
    double AvgDeliveryMinutes,
    List<DeliverymanEfficiencyRowDto> Deliverymen,
    List<string> EvolutionLabels,
    List<int> EvolutionDeliveries,
    List<int> EvolutionFees);
