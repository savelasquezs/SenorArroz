using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public sealed class DeliveryReturnEvidenceTests
{
    private static readonly DateTime Start = new(2026, 10, 16, 16, 0, 0, DateTimeKind.Utc);
    private static decimal Lat(double meters) => 6m + (decimal)(meters / 111_195d);

    private static DeliverymanLocation Point(int seconds, double meters, double accuracy = 10) => new()
    {
        Id = seconds + 1, DeliverymanId = 7, DeliveryRouteId = 8, WorkSessionId = 9,
        RecordedAt = Start.AddSeconds(seconds), Latitude = Lat(meters),
        Longitude = -75m, AccuracyMeters = accuracy, GpsEnabled = true,
        ClientPointId = Guid.NewGuid()
    };

    private static DeliveryEvidenceRules.Visit Stop(int id, double meters = 0) =>
        new(new DeliveryRouteStop { Id = id, OrderId = id, DeliveryRouteId = 8 },
            id, Lat(meters), -75m, Start);

    [Fact]
    public void AStoreStayIsNotAReturn()
    {
        Assert.Null(DeliveryEvidenceRules.FindReturn(
            [Point(0, 0), Point(30, 10), Point(60, 5)],
            6m, -75m, 50, Start.AddSeconds(60)));
    }

    [Fact]
    public void ReturnNeedsReliableDepartureAndTwoStoreReadings()
    {
        var points = new[] { Point(0, 500), Point(30, 10), Point(60, 5) };
        Assert.Null(DeliveryEvidenceRules.FindReturn(
            points[..2], 6m, -75m, 50, Start.AddSeconds(30)));
        var result = DeliveryEvidenceRules.FindReturn(
            points, 6m, -75m, 50, Start.AddSeconds(60));
        Assert.NotNull(result);
        Assert.Equal(points[^1].Id, result.LocationId);
        points[2] = Point(60, 5, 100);
        Assert.Null(DeliveryEvidenceRules.FindReturn(
            points, 6m, -75m, 50, Start.AddSeconds(60)));
    }

    [Fact]
    public void OldArrivalSampleAndOneNewPointCannotConfirmVisit()
    {
        var visit = Stop(1);
        DeliveryEvidenceRules.Advance([visit], Point(0, 20), 50, 120, 15);
        DeliveryEvidenceRules.Advance([visit], Point(300, 20), 50, 120, 15);
        Assert.Null(DeliveryEvidenceRules.Advance(
            [visit], Point(330, 200), 50, 120, 15));
        Assert.Null(visit.Stop.ArrivalConfirmedAtUtc);
    }

    [Fact]
    public void OneDepartureCanStartTheNextDistinctStop()
    {
        var first = Stop(1);
        var second = Stop(2, 300);
        DeliveryEvidenceRules.Advance([first, second], Point(0, 20), 50, 120, 15);
        DeliveryEvidenceRules.Advance([first, second], Point(30, 10), 50, 120, 15);
        var firstExit = DeliveryEvidenceRules.Advance(
            [first, second], Point(60, 300), 50, 120, 15);
        Assert.Equal(1, firstExit?.Visit.OrderId);
        Assert.NotNull(second.Stop.ArrivalCandidateAtUtc);
        DeliveryEvidenceRules.Advance([first, second], Point(90, 310), 50, 120, 15);
        Assert.Equal(2, DeliveryEvidenceRules.Advance(
            [first, second], Point(120, 600), 50, 120, 15)?.Visit.OrderId);
    }

    [Fact]
    public void SameVisitCannotDeliverTwoStopsAtSamePin()
    {
        var first = Stop(1);
        var second = Stop(2);
        var exits = new List<int>();
        foreach (var point in new[] { Point(0, 20), Point(30, 10), Point(60, 300) })
        {
            var result = DeliveryEvidenceRules.Advance([first, second], point, 50, 120, 15);
            if (result is not null) exits.Add(result.Visit.OrderId);
        }
        Assert.Equal([1], exits);
        Assert.Null(second.Stop.ArrivalConfirmedAtUtc);
    }
}
