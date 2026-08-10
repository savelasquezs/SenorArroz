using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class DeliveryRoutingOptimizerTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ApproximateMatrix_IsSymmetricAndUsesRoadFactor()
    {
        var provider = new ApproximateRoutingCostMatrixProvider(Options.Create(new DeliveryRoutingOptions
        {
            ApproximateRoadFactor = 1.3,
            ApproximateUrbanSpeedKph = 20,
        }));
        var matrix = provider.Create(7.1254, -73.1198,
        [
            new RoutingNode(10, 7.1304, -73.1198, Now, Now, true, 240),
            new RoutingNode(11, 7.1254, -73.1148, Now, Now, true, 240),
        ]);

        Assert.Equal(3, matrix.DurationSeconds.GetLength(0));
        Assert.True(matrix.DistanceMeters[0, 1] > 500);
        Assert.Equal(matrix.DistanceMeters[1, 2], matrix.DistanceMeters[2, 1]);
        Assert.Equal(matrix.DurationSeconds[0, 2], matrix.DurationSeconds[2, 0]);
        Assert.True(matrix.BearingFromBranchDegrees[1] <= 10 || matrix.BearingFromBranchDegrees[1] >= 350);
        Assert.InRange(matrix.BearingFromBranchDegrees[2], 80, 100);
    }

    [Fact]
    public void Optimizer_RoutesAllEligibleNodesWithoutStopLimit()
    {
        var options = Options.Create(new DeliveryRoutingOptions
        {
            SolverTimeLimitMs = 500,
            DirectionPenaltyPerDegreeSeconds = 0,
            DroppedOrderBasePenaltySeconds = 100_000,
        });
        var provider = new ApproximateRoutingCostMatrixProvider(options);
        var nodes = Enumerable.Range(0, 12)
            .Select(index => new RoutingNode(
                index + 1,
                7.1254 + index * 0.0002,
                -73.1198 + index * 0.0001,
                Now.AddMinutes(-index),
                Now,
                true,
                60))
            .ToArray();
        var matrix = provider.Create(7.1254, -73.1198, nodes);

        var result = new OrToolsDeliveryRouteOptimizer(options).Optimize(
            new DeliveryRouteOptimizationRequest(matrix, [new RoutingVehicleSlot(0)], Now));

        var route = Assert.Single(result.Routes);
        Assert.Equal(12, route.NodeIndexes.Count);
        Assert.Empty(result.UnroutedNodeIndexes);
        Assert.Equal(Enumerable.Range(0, 12).Order(), route.NodeIndexes.Order());
    }
}
