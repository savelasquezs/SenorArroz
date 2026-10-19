using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Features.DeliveryRouting.DTOs;

public sealed record DeliveryRoutingStopDto(
    int OrderId, int? StopSequence, string Type, string Status, string Address,
    string? AdditionalInfo, string? Neighborhood, DateTime EstimatedReadyAtUtc,
    DateTime? EstimatedArrivalAtUtc, int TravelFromPreviousSeconds, int ServiceSeconds,
    double BearingFromBranchDegrees, bool IsReady, bool IsSuggestedWait, string? UnroutedReason)
{
    public DateTime? ConservativeReadyAtUtc { get; init; }
    public string PreparationConfidence { get; init; } = "unknown";
    public double ReadyWithinWindowProbability { get; init; }
    public int PreparationSampleSize { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime PriorityAnchorUtc { get; init; }
    public DateTime? ReservedForUtc { get; init; }
    public int? DeliveryTimeLimitSeconds { get; init; }
    public string? DeliveryTimeLimitReason { get; init; }
}

public sealed record DeliveryRouteProposalDto(
    int Id, int Sequence, string Status, string Recommendation, DateTime ExpectedDepartureAtUtc,
    int WaitSeconds, int ApproximateDrivingDurationSeconds, int ApproximateDistanceMeters,
    int? ValidatedDrivingDurationSeconds, int? ValidatedDistanceMeters, string GoogleValidationStatus,
    int LastDeliverySeconds, int WorstAgeAtDeliverySeconds, double DirectionSpreadDegrees, long Score,
    bool IsClaimable, bool IsFullyReady, IReadOnlyList<int> ClaimableReadyOrderIds,
    IReadOnlyList<int> SuggestedWaitOrderIds, string? PlanningWarnings, IReadOnlyList<DeliveryRoutingStopDto> Stops)
{
    public DateTime? WaitDeadlineAtUtc { get; init; }
    public string? SelectionFingerprint { get; init; }
}

public sealed record DeliveryRoutingCapacityDto(int AvailableNow, int AvailableSoon)
{
    public IReadOnlyList<DeliveryCourierAvailability> Couriers { get; init; } = [];
}

public sealed record DeliveryRoutingFleetDto(DateTime ObservedAtUtc, int BranchId,
    DeliveryRoutingCapacityDto Capacity, IReadOnlyList<string> Warnings);

public sealed record DeliveryRoutingPlanDto(int Id, long Version, string Status,
    DateTime GeneratedAtUtc, string MatrixSource, DeliveryRoutingCapacityDto Capacity,
    int SolverDurationMs, string? Warnings, IReadOnlyList<DeliveryRouteProposalDto> Proposals,
    IReadOnlyList<DeliveryRoutingStopDto> UnroutedOrders);

public sealed record PreviewDeliveryRouteRequest(IReadOnlyList<int> OrderIds, long? ExpectedPlanVersion = null);

public interface IDeliveryRoutingPlanService
{
    Task<DeliveryRoutingPlanDto> GetOrCreateActivePlanAsync(int branchId, CancellationToken cancellationToken = default);
    Task<DeliveryRoutingPlanDto> RecalculateAsync(int branchId, CancellationToken cancellationToken = default);
    Task<DeliveryRouteProposalDto> PreviewAsync(int branchId, IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default,
        long? expectedPlanVersion = null);
}
