using System.Text.Json;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Dashboard.Services;

/// <summary>
/// Agrega pedidos de domicilio entregados en un rango para el dashboard.
/// </summary>
public static class DeliveryDashboardAggregator
{
    public static DeliveryAggregatesDto Build(
        IReadOnlyList<Order> orders,
        IReadOnlyList<(DateTime UpdatedAt, int Total)> salesTicks,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var (labels, counts, fees, sales) = BuildEvolutionSeries(orders, salesTicks, fromUtc, toUtc);
        var totalFees = orders.Sum(o => (long)(o.DeliveryFee ?? 0));
        var totalSales = salesTicks.Sum(t => (long)t.Total);
        var periodPct = totalSales > 0 ? Math.Round(100d * totalFees / totalSales, 2) : 0d;

        if (orders.Count == 0)
        {
            return new DeliveryAggregatesDto(
                0, 0,
                new List<DeliverymanEfficiencyRowDto>(),
                labels,
                counts,
                fees,
                sales,
                periodPct);
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
                var feesDm = g.Sum(x => x.DeliveryFee ?? 0);
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
                    feesDm);
            })
            .OrderByDescending(x => x.DeliveredCount)
            .ToList();

        return new DeliveryAggregatesDto(avgPrep, avgDel, byDriver, labels, counts, fees, sales, periodPct);
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

    private static (List<string> Labels, List<int> Counts, List<int> Fees, List<long> Sales) BuildEvolutionSeries(
        IReadOnlyList<Order> orders,
        IReadOnlyList<(DateTime UpdatedAt, int Total)> salesTicks,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var buckets = DashboardDeliveryTimeBuckets.Create(fromUtc, toUtc);
        var n = buckets.Count;
        var counts = new int[n];
        var fees = new int[n];
        var sales = new long[n];

        foreach (var o in orders)
        {
            var i = buckets.GetBucketIndex(o.UpdatedAt);
            if ((uint)i >= (uint)n)
                continue;
            counts[i]++;
            fees[i] += o.DeliveryFee ?? 0;
        }

        foreach (var t in salesTicks)
        {
            var i = buckets.GetBucketIndex(t.UpdatedAt);
            if ((uint)i >= (uint)n)
                continue;
            sales[i] += t.Total;
        }

        return (buckets.Labels.ToList(), counts.ToList(), fees.ToList(), sales.ToList());
    }
}

public record DeliverymanEfficiencyRowDto(
    int Id,
    int BranchId,
    string Name,
    int DeliveredCount,
    double AvgDeliveryMinutes,
    int DeliveryFeeTotal,
    int RouteCompletedCount = 0,
    double? RouteOnTimePercent = null,
    double AvgRouteActualMinutes = 0);

public record DeliveryAggregatesDto(
    double AvgPrepMinutes,
    double AvgDeliveryMinutes,
    List<DeliverymanEfficiencyRowDto> Deliverymen,
    List<string> EvolutionLabels,
    List<int> EvolutionDeliveries,
    List<int> EvolutionFees,
    List<long> EvolutionSalesTotals,
    double PeriodFeeToSalesPercent);
