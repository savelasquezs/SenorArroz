using SenorArroz.Application.Features.Orders.Queries;

namespace SenorArroz.Tests;

public class DeliveryPreparationLocationGateTests
{
    [Fact]
    public void SamePoint_IsInsideDefaultBranchRadius()
    {
        var isInside = DeliveryPreparationLocationGate.IsInside(
            4.609710m, -74.081750m,
            4.609710m, -74.081750m,
            50,
            out var distance);

        Assert.InRange(distance, 0, 0.01);
        Assert.True(isInside);
    }

    [Fact]
    public void PointBeyondConfiguredRadius_IsOutsideBranch()
    {
        var isInside = DeliveryPreparationLocationGate.IsInside(
            4.609710m, -74.081750m,
            4.610710m, -74.081750m,
            50,
            out var distance);

        Assert.True(distance > 50);
        Assert.False(isInside);
    }
}
