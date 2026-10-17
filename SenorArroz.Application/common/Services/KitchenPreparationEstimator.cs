using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;
using System.Text.Json;

namespace SenorArroz.Application.Common.Services;

public sealed class KitchenPreparationEstimator(IApplicationDbContext db,
    IOptions<DeliveryRoutingOptions> options) : IKitchenPreparationEstimator
{
    public async Task<IReadOnlyDictionary<int, KitchenPreparationEstimate>> EstimateAsync(int branchId,
        IReadOnlyCollection<int> orderIds, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0) return new Dictionary<int, KitchenPreparationEstimate>();
        var settings = options.Value;
        var cutoff = nowUtc.AddDays(-Math.Max(1, settings.PreparationHistoryDays));
        var history = await db.Orders.AsNoTracking()
            .Where(o => o.BranchId == branchId && o.CreatedAt >= cutoff && o.CreatedAt <= nowUtc
                && o.Status != OrderStatus.Cancelled && !orderIds.Contains(o.Id))
            .OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(settings.PreparationMaximumHistoryOrders, 100, 10000))
            .Select(o => new Observation(o.CreatedAt, o.PrepareAt, o.Type, o.Status, o.StatusTimes,
                o.OrderDetails.Sum(d => d.Quantity), o.OrderDetails.Select(d => d.ProductId).ToArray()))
            .ToListAsync(cancellationToken);
        var current = await db.Orders.AsNoTracking().Where(o => o.BranchId == branchId && orderIds.Contains(o.Id))
            .Select(o => new { o.Id, Data = new Observation(o.CreatedAt, o.PrepareAt, o.Type, o.Status, o.StatusTimes,
                o.OrderDetails.Sum(d => d.Quantity), o.OrderDetails.Select(d => d.ProductId).ToArray()) })
            .ToListAsync(cancellationToken);
        var minimum = Math.Max(1, settings.PreparationMinimumSampleSize);
        var samples = history.Select(o => (Data: o, Times: Times(o.StatusTimes)))
            .Where(o => o.Times.TryGetValue("ready", out var ready) && ready <= nowUtc).ToArray();
        var result = new Dictionary<int, KitchenPreparationEstimate>();
        foreach (var order in current)
        {
            var item = order.Data;
            if (item.Status == OrderStatus.Ready)
            {
                result[order.Id] = new(nowUtc, "confirmed") { ReadyWithinWindowProbability = 1 };
                continue;
            }
            var stage = item.Status == OrderStatus.InPreparation ? "inpreparation" : "taken";
            var anchor = Anchor(item, Times(item.StatusTimes), stage);
            var elapsed = Math.Max(0, (nowUtc - anchor).TotalSeconds);
            var durations = samples.Select(s => (s.Data, Seconds: (s.Times["ready"] - Anchor(s.Data, s.Times, stage)).TotalSeconds))
                .Where(s => s.Seconds is > 0 and <= 14400).ToArray();
            var matched = durations.Where(s => QuantityBucket(s.Data.Quantity) == QuantityBucket(item.Quantity)
                && Math.Abs(s.Data.CreatedAt.Hour - item.CreatedAt.Hour) <= 2
                && (item.Products.Length == 0 || s.Data.Products.Intersect(item.Products).Any())).ToArray();
            var selected = matched.Length >= minimum ? matched : durations;
            var survivors = selected.Where(s => s.Seconds > elapsed).Select(s => s.Seconds).Order().ToArray();
            var enough = selected.Length >= minimum && survivors.Length >= Math.Min(5, minimum);
            var fallback = Math.Max(60, settings.PreparationFallbackSeconds);
            var remaining = survivors.Length > 0 ? Math.Max(30, Quantile(survivors, .5) - elapsed)
                : elapsed < fallback ? fallback - elapsed : Math.Max(60, fallback / 4);
            var eta = anchor.AddSeconds(elapsed + remaining);
            var within = survivors.Count(s => s <= elapsed + settings.PreparationLookaheadSeconds);
            var probability = enough ? within / (double)survivors.Length
                : survivors.Length > 0 ? (within + 1d) / (survivors.Length + 2) : .5;
            result[order.Id] = new KitchenPreparationEstimate(eta, enough ? matched.Length >= minimum ? "high" : "medium" : "low")
            {
                ConservativeReadyAtUtc = anchor.AddSeconds(elapsed + (survivors.Length > 0
                    ? Math.Max(60, Quantile(survivors, .9) - elapsed) : remaining * 2)),
                ReadyWithinWindowProbability = Math.Clamp(probability, 0, 1), SampleSize = selected.Length
            };
        }
        return result;
    }

    private sealed record Observation(DateTime CreatedAt, DateTime? PrepareAt, OrderType? Type,
        OrderStatus Status, string StatusTimes, int Quantity, int[] Products);

    private static DateTime Anchor(Observation order, Dictionary<string, DateTime> times, string stage) =>
        times.TryGetValue(stage, out var at) ? at
            : order.Type == OrderType.Reservation ? order.PrepareAt ?? order.CreatedAt : order.CreatedAt;

    private static Dictionary<string, DateTime> Times(string value)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(value) ?? []; }
        catch (JsonException) { return []; }
    }

    private static int QuantityBucket(int quantity) => quantity <= 2 ? 0 : quantity <= 5 ? 1 : 2;
    private static double Quantile(double[] ordered, double percentile) => ordered[(int)Math.Ceiling((ordered.Length - 1) * percentile)];
}
