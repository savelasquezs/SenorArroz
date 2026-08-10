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
    public int PreparationFallbackSeconds { get; set; } = 1800;
    public double ApproximateRoadFactor { get; set; } = 1.3;
    public double ApproximateUrbanSpeedKph { get; set; } = 22;
    public int DirectionPenaltyPerDegreeSeconds { get; set; } = 3;
    public int DroppedOrderBasePenaltySeconds { get; set; } = 3600;
    public int MaximumFinalistsToValidate { get; set; } = 12;
    public int ServiceSecondsPerOrder { get; set; } = 240;
}
