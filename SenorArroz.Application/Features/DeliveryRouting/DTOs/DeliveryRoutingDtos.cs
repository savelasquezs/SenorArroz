namespace SenorArroz.Application.Features.DeliveryRouting.DTOs;

public sealed record DeliveryRoutingStopDto(
    int OrderId,
    int? StopSequence,
    string Type,
    string Status,
    string Address,
    string? AdditionalInfo,
    string? Neighborhood,
    DateTime EstimatedReadyAtUtc,
    DateTime? EstimatedArrivalAtUtc,
    int TravelFromPreviousSeconds,
    int ServiceSeconds,
    double BearingFromBranchDegrees,
    bool IsReady,
    bool IsSuggestedWait,
    string? UnroutedReason);

public sealed record DeliveryRouteProposalDto(
    int Id,
    int Sequence,
    string Status,
    string Recommendation,
    DateTime ExpectedDepartureAtUtc,
    int WaitSeconds,
    int ApproximateDrivingDurationSeconds,
    int ApproximateDistanceMeters,
    int? ValidatedDrivingDurationSeconds,
    int? ValidatedDistanceMeters,
    string GoogleValidationStatus,
    int LastDeliverySeconds,
    int WorstAgeAtDeliverySeconds,
    double DirectionSpreadDegrees,
    long Score,
    bool IsClaimable,
    bool IsFullyReady,
    IReadOnlyList<int> ClaimableReadyOrderIds,
    IReadOnlyList<int> SuggestedWaitOrderIds,
    string? PlanningWarnings,
    IReadOnlyList<DeliveryRoutingStopDto> Stops);

public sealed record DeliveryRoutingCapacityDto(int AvailableNow, int AvailableSoon);

public sealed record DeliveryRoutingPlanDto(
    int Id,
    long Version,
    string Status,
    DateTime GeneratedAtUtc,
    string MatrixSource,
    DeliveryRoutingCapacityDto Capacity,
    int SolverDurationMs,
    string? Warnings,
    IReadOnlyList<DeliveryRouteProposalDto> Proposals,
    IReadOnlyList<DeliveryRoutingStopDto> UnroutedOrders);

public sealed record PreviewDeliveryRouteRequest(IReadOnlyList<int> OrderIds);

public interface IDeliveryRoutingPlanService
{
    Task<DeliveryRoutingPlanDto> GetOrCreateActivePlanAsync(int branchId, CancellationToken cancellationToken = default);
    Task<DeliveryRoutingPlanDto> RecalculateAsync(int branchId, CancellationToken cancellationToken = default);
    Task<DeliveryRouteProposalDto> PreviewAsync(int branchId, IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default);
}
