namespace SenorArroz.Application.Features.Dashboard.Services;

/// <summary>
/// Agrega rutas domicilio completadas para KPIs y series alineadas a <see cref="DashboardDeliveryTimeBuckets"/>.
/// </summary>
public static class DeliveryRouteDashboardAggregator
{
    public sealed record RouteMetricRow(
        int DeliverymanId,
        DateTime CompletedAtUtc,
        int? ActualDurationSeconds,
        int? MetaDurationSeconds,
        bool? MetSla,
        int? PlannedDistanceMeters,
        int? ReturnToBranchMeters);

    public static DeliveryRouteMetricsDto Build(
        IReadOnlyList<RouteMetricRow> routes,
        DashboardDeliveryTimeBuckets buckets)
    {
        var n = buckets.Count;
        var bucketRoutes = Enumerable.Range(0, n).Select(_ => new List<RouteMetricRow>()).ToList();

        foreach (var r in routes)
        {
            var i = buckets.GetBucketIndex(r.CompletedAtUtc);
            if ((uint)i < (uint)n)
                bucketRoutes[i].Add(r);
        }

        var evolutionRoutesCompleted = new List<int>();
        var evolutionOnTimePercent = new List<double?>();
        var evolutionDelayedPercent = new List<double?>();
        var evolutionAvgDelayMinutes = new List<double?>();
        var evolutionAvgActualRouteMinutes = new List<double?>();

        for (var i = 0; i < n; i++)
        {
            var br = bucketRoutes[i];
            evolutionRoutesCompleted.Add(br.Count);

            var slaSubset = br.Where(x => x.MetSla.HasValue).ToList();
            if (slaSubset.Count > 0)
            {
                var onTime = slaSubset.Count(x => x.MetSla == true);
                var pct = Math.Round(100d * onTime / slaSubset.Count, 2);
                evolutionOnTimePercent.Add(pct);
                evolutionDelayedPercent.Add(Math.Round(100d - pct, 2));
            }
            else
            {
                evolutionOnTimePercent.Add(null);
                evolutionDelayedPercent.Add(null);
            }

            var delayedVariances = br
                .Where(x => x.ActualDurationSeconds is { } act && x.MetaDurationSeconds is { } meta)
                .Select(x => x.ActualDurationSeconds!.Value - x.MetaDurationSeconds!.Value)
                .Where(v => v > 0)
                .Select(v => v / 60d)
                .ToList();
            evolutionAvgDelayMinutes.Add(
                delayedVariances.Count > 0 ? Math.Round(delayedVariances.Average(), 2) : (double?)null);

            var actualMins = br
                .Where(x => x.ActualDurationSeconds.HasValue)
                .Select(x => x.ActualDurationSeconds!.Value / 60d)
                .ToList();
            evolutionAvgActualRouteMinutes.Add(
                actualMins.Count > 0 ? Math.Round(actualMins.Average(), 2) : (double?)null);
        }

        var withSla = routes.Where(r => r.MetSla.HasValue).ToList();
        var onTimeCount = withSla.Count(r => r.MetSla == true);

        var delayedVariancesAll = routes
            .Where(r => r.ActualDurationSeconds.HasValue && r.MetaDurationSeconds.HasValue)
            .Select(r => r.ActualDurationSeconds!.Value - r.MetaDurationSeconds!.Value)
            .Where(v => v > 0)
            .Select(v => v / 60d)
            .ToList();

        var actualAll = routes.Where(r => r.ActualDurationSeconds.HasValue)
            .Select(r => r.ActualDurationSeconds!.Value / 60d).ToList();
        var metaAll = routes.Where(r => r.MetaDurationSeconds.HasValue)
            .Select(r => r.MetaDurationSeconds!.Value / 60d).ToList();

        var distMeters = routes.Sum(r =>
            (r.PlannedDistanceMeters ?? 0) + (r.ReturnToBranchMeters ?? 0));

        var perDriver = routes
            .GroupBy(r => r.DeliverymanId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var list = g.ToList();
                    var slaL = list.Where(x => x.MetSla.HasValue).ToList();
                    double? otp = null;
                    if (slaL.Count > 0)
                        otp = Math.Round(100d * slaL.Count(x => x.MetSla == true) / slaL.Count, 2);
                    var act = list.Where(x => x.ActualDurationSeconds.HasValue)
                        .Select(x => x.ActualDurationSeconds!.Value / 60d).ToList();
                    var avgAct = act.Count > 0 ? Math.Round(act.Average(), 2) : 0d;
                    return (Count: list.Count, OnTimePercent: otp, AvgActual: avgAct);
                });

        return new DeliveryRouteMetricsDto(
            routes.Count,
            withSla.Count,
            withSla.Count > 0 ? Math.Round(100d * onTimeCount / withSla.Count, 2) : 0,
            withSla.Count > 0 ? Math.Round(100d - 100d * onTimeCount / withSla.Count, 2) : 0,
            actualAll.Count > 0 ? Math.Round(actualAll.Average(), 2) : 0,
            metaAll.Count > 0 ? Math.Round(metaAll.Average(), 2) : 0,
            delayedVariancesAll.Count > 0 ? Math.Round(delayedVariancesAll.Average(), 2) : 0,
            Math.Round(distMeters / 1000d, 3),
            evolutionRoutesCompleted,
            evolutionOnTimePercent,
            evolutionDelayedPercent,
            evolutionAvgDelayMinutes,
            evolutionAvgActualRouteMinutes,
            perDriver);
    }
}

public sealed record DeliveryRouteMetricsDto(
    int CompletedRoutesCount,
    int RoutesWithSlaDataCount,
    double PeriodOnTimePercent,
    double PeriodDelayedPercent,
    double AvgActualRouteMinutes,
    double AvgMetaRouteMinutes,
    double AvgDelayMinutesWhenDelayed,
    double TotalDistanceKm,
    List<int> EvolutionRoutesCompleted,
    List<double?> EvolutionOnTimePercent,
    List<double?> EvolutionDelayedPercent,
    List<double?> EvolutionAvgDelayMinutes,
    List<double?> EvolutionAvgActualRouteMinutes,
    Dictionary<int, (int Count, double? OnTimePercent, double AvgActual)> PerDriver);
