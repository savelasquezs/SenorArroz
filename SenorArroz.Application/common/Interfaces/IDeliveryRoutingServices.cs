using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Interfaces;

public sealed record RoutingNode(
    int OrderId,
    double Latitude,
    double Longitude,
    DateTime PriorityAnchorUtc,
    DateTime EstimatedReadyAtUtc,
    bool IsReady,
    int ServiceSeconds);

public sealed record RoutingVehicleSlot(int AvailableAtSeconds);

public sealed record RoutingCostMatrix(
    IReadOnlyList<RoutingNode> Nodes,
    long[,] DurationSeconds,
    long[,] DistanceMeters,
    double[] BearingFromBranchDegrees,
    RoutingMatrixSource Source,
    DateTime GeneratedAtUtc,
    IReadOnlyList<string> Warnings);

public interface IRoutingCostMatrixProvider
{
    RoutingCostMatrix Create(
        double branchLatitude,
        double branchLongitude,
        IReadOnlyList<RoutingNode> nodes);
}

public sealed record DeliveryRouteOptimizationRequest(
    RoutingCostMatrix Matrix,
    IReadOnlyList<RoutingVehicleSlot> Vehicles,
    DateTime GeneratedAtUtc);

public sealed record OptimizedRoute(
    int VehicleIndex,
    IReadOnlyList<int> NodeIndexes,
    long ApproximateDurationSeconds,
    long ApproximateDistanceMeters,
    long Score);

public sealed record DeliveryRouteOptimizationResult(
    IReadOnlyList<OptimizedRoute> Routes,
    IReadOnlyList<int> UnroutedNodeIndexes,
    int SolverDurationMs,
    IReadOnlyList<string> Warnings);

public interface IDeliveryRouteOptimizer
{
    DeliveryRouteOptimizationResult Optimize(DeliveryRouteOptimizationRequest request);
}

public sealed record KitchenPreparationEstimate(DateTime EstimatedReadyAtUtc, string Confidence);

public interface IKitchenPreparationEstimator
{
    Task<IReadOnlyDictionary<int, KitchenPreparationEstimate>> EstimateAsync(
        int branchId,
        IReadOnlyCollection<int> orderIds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed record DeliveryRoutingCapacity(
    IReadOnlyList<RoutingVehicleSlot> Slots,
    int AvailableNow,
    int AvailableSoon,
    IReadOnlyList<string> Warnings);

public interface IDeliverymanAvailabilityService
{
    Task<DeliveryRoutingCapacity> GetCapacityAsync(
        int branchId,
        double branchLatitude,
        double branchLongitude,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
