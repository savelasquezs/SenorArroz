using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public sealed class KitchenPreparationEstimator : IKitchenPreparationEstimator
{
    private readonly IApplicationDbContext _db;
    private readonly DeliveryRoutingOptions _options;

    public KitchenPreparationEstimator(IApplicationDbContext db, IOptions<DeliveryRoutingOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<IReadOnlyDictionary<int, KitchenPreparationEstimate>> EstimateAsync(
        int branchId,
        IReadOnlyCollection<int> orderIds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc.AddDays(-Math.Max(1, _options.PreparationHistoryDays));
        var history = await _db.Orders
            .AsNoTracking()
            .Where(x => x.BranchId == branchId && x.CreatedAt >= cutoff)
            .Select(x => new { x.CreatedAt, x.PrepareAt, x.StatusTimes })
            .ToListAsync(cancellationToken);

        var samples = history
            .Select(x => PreparationSeconds(x.StatusTimes, x.PrepareAt ?? x.CreatedAt))
            .Where(x => x is > 0 and <= 14_400)
            .Select(x => x!.Value)
            .ToList();
        var hasEnoughSamples = samples.Count >= Math.Max(1, _options.PreparationMinimumSampleSize);
        var preparationSeconds = hasEnoughSamples
            ? (int)Math.Round(samples.Average())
            : Math.Max(60, _options.PreparationFallbackSeconds);

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(x => x.BranchId == branchId && orderIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type, x.Status, x.CreatedAt, x.PrepareAt })
            .ToListAsync(cancellationToken);

        return orders.ToDictionary(
            x => x.Id,
            x => new KitchenPreparationEstimate(
                x.Status == OrderStatus.Ready
                    ? nowUtc
                    : (x.Type == OrderType.Reservation ? x.PrepareAt ?? x.CreatedAt : x.CreatedAt)
                      .AddSeconds(preparationSeconds) is var estimated && estimated > nowUtc
                        ? estimated
                        : nowUtc,
                hasEnoughSamples ? "high" : "low"));
    }

    private static int? PreparationSeconds(string statusTimes, DateTime anchor)
    {
        try
        {
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DateTime>>(statusTimes);
            if (values is null || !values.TryGetValue("ready", out var ready))
                return null;
            return (int)Math.Round((ready - anchor).TotalSeconds);
        }
        catch
        {
            return null;
        }
    }
}
