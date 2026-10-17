namespace SenorArroz.Application.Options;

public class DeliveryRoutingOptions
{
    public const string SectionName = "DeliveryRouting";

    public bool Enabled { get; set; } = true;
    public bool ShadowMode { get; set; } = true;
    public int SoonAvailableThresholdSeconds { get; set; } = 600;
    public int KitchenWaitReferenceSeconds { get; set; } = 180;
    public int SoftDetourReferenceSeconds { get; set; } = 600;
    public int SoftLastDeliveryTargetSeconds { get; set; } = 2100;
    public int SolverTimeLimitMs { get; set; } = 1000;
    public int ActiveGpsFreshnessSeconds { get; set; } = 180;
    public int PreparationHistoryDays { get; set; } = 30;
    public int PreparationMinimumSampleSize { get; set; } = 10;
    public int PreparationMaximumHistoryOrders { get; set; } = 2000;
    public int PreparationFallbackSeconds { get; set; } = 1800;
    public double ApproximateRoadFactor { get; set; } = 1.3;
    public double ApproximateUrbanSpeedKph { get; set; } = 22;
    public int DirectionPenaltyPerDegreeSeconds { get; set; } = 3;
    public int DroppedOrderBasePenaltySeconds { get; set; } = 3600;
    public int MaximumFinalistsToValidate { get; set; } = 12;
    public int ServiceSecondsPerOrder { get; set; } = 240;
    public int RouteDispatchCostSeconds { get; set; } = 120;
    public int PreparationLookaheadSeconds { get; set; } = 900;
    public int PlanMaxAgeSeconds { get; set; } = 120;
    public RoutingMeasurementOptions Measurement { get; set; } = new();
}

/// <summary>Temporary P0 experiment; remove after the documented 14-day window.</summary>
public sealed class RoutingMeasurementOptions
{
    public bool Enabled { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsActive(DateTime utcNow) => Enabled
        && StartedAtUtc is { } start && ExpiresAtUtc is { } end
        && start.Offset == TimeSpan.Zero && end.Offset == TimeSpan.Zero
        && end > start && end - start <= TimeSpan.FromDays(14)
        && utcNow >= start.UtcDateTime && utcNow < end.UtcDateTime;
}
