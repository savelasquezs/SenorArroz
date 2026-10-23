using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Queries;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using static SenorArroz.Application.Features.Dashboard.Services.DeliveryPerformanceAnalytics;

namespace SenorArroz.Tests;

public class DeliveryPerformanceAnalyticsTests
{
    private static readonly DateTime Start = new(2026, 10, 22, 17, 0, 0, DateTimeKind.Utc);

    private static DeliveryRoute Route(int id = 1, int minutes = 20) => new()
    {
        Id = id, DeliverymanId = 7, WorkSessionId = 3, DepartedAtUtc = Start,
        CompletedAtUtc = Start.AddMinutes(minutes), Status = DeliveryRouteStatus.Completed,
        Branch = new Branch { DeliveryTrackingActiveIntervalSeconds = 30 },
    };

    private static DeliverymanLocation Point(int seconds, decimal longitude = -75.58m) => new()
    {
        DeliveryRouteId = 1, DeliverymanId = 7, WorkSessionId = 3,
        RecordedAt = Start.AddSeconds(seconds), Latitude = 6.24m, Longitude = longitude,
        AccuracyMeters = 10, GpsEnabled = true,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnAfterWorkSessionEndIsInconsistentEvenForHistoricalUnboundRoutes(bool bound)
    {
        var route = Route();
        route.WorkSessionId = bound ? 3 : null;
        route.CompletedAtUtc = Start.AddDays(1);
        var session = new DeliveryWorkSession { Id = 3, DeliverymanId = 7,
            StartedAt = Start.AddHours(-1), AutoCloseAt = Start.AddHours(12), EndedAt = Start.AddHours(2) };
        Assert.True(IsReturnInconsistent(route, [session]));
        route.CompletedAtUtc = session.EndedAt;
        Assert.False(IsReturnInconsistent(route, [session]));
        session.EndedAt = null;
        route.CompletedAtUtc = session.AutoCloseAt.AddSeconds(1);
        Assert.True(IsReturnInconsistent(route, [session]));
    }

    [Fact]
    public void SameWorkSessionCanCrossColombiaMidnightAndLongDurationsAreNotArbitrarilyRemoved()
    {
        var route = Route();
        route.DepartedAtUtc = new DateTime(2026, 10, 23, 4, 30, 0, DateTimeKind.Utc);
        route.CompletedAtUtc = route.DepartedAtUtc.Value.AddHours(6);
        var session = new DeliveryWorkSession { Id = 3, DeliverymanId = 7,
            StartedAt = route.DepartedAtUtc.Value.AddHours(-1), AutoCloseAt = route.CompletedAtUtc.Value.AddHours(1) };
        Assert.False(IsReturnInconsistent(route, [session]));
        Assert.True(IsReturnInconsistent(route, []));
        route.CompletedAtUtc = route.DepartedAtUtc.Value.AddMinutes(10);
        Assert.False(IsReturnInconsistent(route, []));
        route.DepartedAtUtc = new DateTime(2026, 10, 22, 23, 50, 0, DateTimeKind.Utc);
        route.CompletedAtUtc = route.DepartedAtUtc.Value.AddMinutes(20);
        Assert.False(IsReturnInconsistent(route, []));
    }

    [Fact]
    public async Task InconsistentReturnsAreExcludedFromAllTimeAggregatesAndExposedInHistoryAndDetail()
    {
        await using var db = TestDatabase();
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Sucursal" };
        foreach (var driverId in new[] { 7, 8 })
        {
            var driver = new User { Id = driverId, TenantId = 1, BranchId = 1, Branch = branch,
                Role = UserRole.Deliveryman, Name = $"D{driverId}" };
            var session = new DeliveryWorkSession { Id = driverId, TenantId = 1, DeliverymanId = driverId,
                Deliveryman = driver, BranchId = 1, Branch = branch, StartedAt = Start.AddHours(-1),
                EndedAt = Start.AddHours(2), AutoCloseAt = Start.AddHours(12) };
            db.DeliveryWorkSessions.Add(session);
            foreach (var invalid in new[] { false, true })
            {
                var id = driverId * 10 + (invalid ? 1 : 0);
                var route = Route(id, driverId == 7 ? 20 : 40);
                route.TenantId = 1; route.BranchId = 1; route.Branch = branch;
                route.DeliverymanId = driverId; route.Deliveryman = driver; route.WorkSessionId = null;
                route.DepartedAtUtc = null; route.PlannedDistanceMeters = 1000; route.ReturnToBranchMeters = 1000;
                route.MetSla = true;
                if (invalid) route.CompletedAtUtc = Start.AddDays(1);
                var order = new Order { Id = id, TenantId = 1, BranchId = 1, Branch = branch,
                    DeliveryManId = driverId, DeliveryMan = driver, Type = OrderType.Delivery, Status = OrderStatus.Delivered,
                    CreatedAt = Start, UpdatedAt = Start.AddMinutes(10),
                    StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ready = Start.AddMinutes(-5), ontheway = Start, delivered = Start.AddMinutes(10) }) };
                route.Stops.Add(new DeliveryRouteStop { Id = id, TenantId = 1, OrderId = id, Order = order,
                    DeliveryRouteId = id, StopSequence = 1 });
                db.DeliveryRoutes.Add(route);
            }
        }
        await db.SaveChangesAsync();
        var result = await BuildAsync(db, 1, 7, Start, Start.AddDays(1).AddHours(1), CancellationToken.None);
        Assert.Equal(2, result.Current.CompletedRoutesCount);
        Assert.Equal(1, result.Current.InconsistentReturnCount);
        Assert.Equal(1, result.Current.RouteSampleCount);
        Assert.Equal(20d, result.Current.AvgRouteMinutes);
        Assert.Equal(10d, result.Current.AvgReturnMinutes);
        Assert.Equal(10d, result.Current.MinutesPerKm);
        Assert.Equal(2, result.Current.OrderSampleCount);
        Assert.Equal(10d, result.Current.AvgOrderMinutes);
        Assert.Equal(2, result.Current.SlaSampleCount);
        Assert.Equal(1, result.Team!.InconsistentReturnCount);
        Assert.Equal(40d, result.Team.AvgRouteMinutes);
        Assert.Equal(1, result.Evolution.Sum(x => x.RouteSampleCount));
        Assert.Equal(1, result.Evolution.Sum(x => x.InconsistentReturnCount));
        var recent = Assert.Single(result.RecentRoutes, x => x.Id == 71);
        Assert.True(recent.ReturnIsInconsistent);
        Assert.Null(recent.DurationMinutes);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.Role).Returns("Superadmin");
        var detail = await new GetDashboardDeliveryRouteStopsHandler(db, currentUser.Object)
            .Handle(new GetDashboardDeliveryRouteStopsQuery { RouteId = 71, BranchId = 1 }, CancellationToken.None);
        Assert.True(detail.ReturnIsInconsistent);
        Assert.Equal(Start.AddDays(1), detail.CompletedAtUtc);
        Assert.Equal(Start.AddMinutes(10), Assert.Single(detail.Stops).DeliveredAtUtc);
    }

    [Fact]
    public void EmptySamplesRemainUnknownInsteadOfZero()
    {
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], []);
        Assert.Null(summary.OnTimePercent);
        Assert.Null(summary.AvgOrderMinutes);
        Assert.Null(summary.AvgRouteMinutes);
        Assert.Null(summary.MinutesPerKm);
        Assert.Equal(0, summary.DeliveredCount);
    }

    [Fact]
    public void RouteAssignmentDoesNotSubstituteForObservedDeparture()
    {
        var route = Route();
        route.DepartedAtUtc = null;
        route.RouteStartedAtUtc = Start;
        route.ActualDurationSeconds = 1200;
        route.MetSla = true;
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(route, new([], 0, 0, false))]);
        Assert.Null(summary.AvgRouteMinutes);
        Assert.Equal(100d, summary.OnTimePercent);
        Assert.Equal(1, summary.SlaSampleCount);
        Assert.Empty(AnalyzeTrack(route, [Point(0), Point(60)]).Segments);
    }

    [Fact]
    public void AssignedRoutesUseEarliestValidOnTheWayAsRealDeparture()
    {
        var observed = Route(1, 10);
        var reference = Route(2, 40);
        reference.DepartedAtUtc = null;
        reference.RouteStartedAtUtc = Start.AddMinutes(5);
        foreach (var minutes in new[] { 10, 0 })
            reference.Stops.Add(new DeliveryRouteStop { Order = new Order { StatusTimes = System.Text.Json.JsonSerializer.Serialize(
                new { ontheway = Start.AddMinutes(minutes), delivered = Start.AddMinutes(20) }) } });
        var samples = new List<RouteSample> { new(observed, new([], 0, 0, false)), new(reference, new([], 0, 0, false)) };
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], samples);
        Assert.Equal(25d, summary.AvgRouteMinutes);
        Assert.Equal(2, summary.RouteSampleCount);
        Assert.Equal((Start, true), RouteTiming(reference));
        Assert.Null(reference.DepartedAtUtc);
        Assert.Null(summary.AvgOrderMinutes);
        Assert.Null(summary.MinutesPerKm);
        Assert.Equal(0, summary.GpsSampleCount);
        observed.Stops = reference.Stops;
        observed.DepartedAtUtc = Start.AddMinutes(5);
        Assert.Equal((Start.AddMinutes(5), false), RouteTiming(observed));
    }

    [Fact]
    public void InvalidOrderDepartureIsNotSubstitutedByOperationalClockAndZeroIsValid()
    {
        var route = Route(minutes: 20);
        route.DepartedAtUtc = null;
        route.Stops.Add(new DeliveryRouteStop { Order = new Order { StatusTimes = "{\"ontheway\":\"invalid\"}" } });
        route.Stops.Add(new DeliveryRouteStop { Order = new Order { StatusTimes = System.Text.Json.JsonSerializer.Serialize(
            new { ontheway = Start.AddMinutes(30), delivered = Start.AddMinutes(10) }) } });
        Assert.Null(RouteTiming(route).Start);
        route.Stops.Add(new DeliveryRouteStop { Order = new Order { StatusTimes = System.Text.Json.JsonSerializer.Serialize(
            new { ontheway = Start.AddMinutes(20), delivered = Start.AddMinutes(20) }) } });
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(route, new([], 0, 0, false))]);
        Assert.Equal(0d, summary.AvgRouteMinutes);
        Assert.Equal(1, summary.RouteSampleCount);
    }

    [Fact]
    public void OrderTimesUseEachDeliveryAndNearestRankP90()
    {
        var orders = Enumerable.Range(1, 10).Select(i => new OrderSample(new Order(), Start.AddMinutes(i * 2), Start)).ToList();
        orders.Add(new(new Order(), Start, null));
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>(orders, []);
        Assert.Equal(11, summary.DeliveredCount);
        Assert.Equal(10, summary.OrderSampleCount);
        Assert.Equal(11d, summary.AvgOrderMinutes);
        Assert.Equal(18d, summary.P90OrderMinutes);
    }

    [Fact]
    public void KilometreRatioUsesPairedTotalsRatherThanMeanOfRatios()
    {
        var a = Route(1, 10);
        var b = Route(2, 30);
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(a, new([], 100, 1, true)), new(b, new([], 100, 9, true))]);
        Assert.Equal(4d, summary.MinutesPerKm);
        Assert.Equal(10d, summary.TotalGpsKm);
        Assert.Equal(2, summary.GpsSampleCount);
    }

    [Fact]
    public void PlannedDistanceIsUsedOnlyWithoutUsableGpsAndIncludesReturn()
    {
        var observed = Route(1, 10);
        observed.PlannedDistanceMeters = 100000;
        observed.ReturnToBranchMeters = 100000;
        var planned = Route(2, 30);
        planned.DepartedAtUtc = null;
        planned.PlannedDistanceMeters = 6000;
        planned.ReturnToBranchMeters = 3000;
        planned.Stops.Add(new DeliveryRouteStop { Order = new Order { StatusTimes = System.Text.Json.JsonSerializer.Serialize(
            new { ontheway = Start, delivered = Start.AddMinutes(20) }) } });
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(observed, new([], 100, 1, true)), new(planned, new([], 30, 2, false))]);
        Assert.Equal(4d, summary.MinutesPerKm);
        Assert.Equal(10d, summary.GpsMinutesPerKm);
        Assert.Equal(3.33d, summary.EstimatedMinutesPerKm);
        Assert.Equal(2, summary.DistanceSampleCount);
        Assert.Equal(1, summary.EstimatedDistanceSampleCount);
        Assert.Equal(1, summary.GpsSampleCount);
        Assert.Equal(1d, summary.TotalGpsKm);
        Assert.Null(planned.DepartedAtUtc);
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(0, 1000)]
    [InlineData(-10, 1000)]
    [InlineData(1000, null)]
    [InlineData(1000, -10)]
    public void UnknownOrInvalidPlannedDistanceCannotBecomeAnEstimatedKilometreSample(int? outbound, int? returning)
    {
        var route = Route();
        route.PlannedDistanceMeters = outbound;
        route.ReturnToBranchMeters = returning;
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(route, new([], 20, 1, false))]);
        Assert.Null(summary.MinutesPerKm);
        Assert.Null(summary.EstimatedMinutesPerKm);
        Assert.Equal(0, summary.DistanceSampleCount);
    }

    [Fact]
    public void PlannedKilometresStillRequireValidTimeAndPreserveZeroDuration()
    {
        var route = Route(minutes: 0);
        route.PlannedDistanceMeters = 1000;
        route.ReturnToBranchMeters = 0;
        var samples = new List<RouteSample> { new(route, new([], 0, 0, false)) };
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], samples);
        Assert.Equal(0d, summary.EstimatedMinutesPerKm);
        Assert.Equal(1, summary.EstimatedDistanceSampleCount);
        route.CompletedAtUtc = Start.AddMinutes(-1);
        Assert.Null(Summarize<DashboardDeliveryPerformanceSummaryDto>([], samples).MinutesPerKm);
        route.DepartedAtUtc = null;
        Assert.Null(Summarize<DashboardDeliveryPerformanceSummaryDto>([], samples).MinutesPerKm);
    }

    [Fact]
    public void StageBreakdownUsesRegisteredOrAssignedDeparture()
    {
        var observed = Route(1, 30);
        observed.Stops.Add(new DeliveryRouteStop { Order = new Order { Status = OrderStatus.Delivered,
            StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ready = Start.AddMinutes(-10), ontheway = Start.AddMinutes(-5), delivered = Start.AddMinutes(20) }) } });
        var reference = Route(2, 50);
        reference.DepartedAtUtc = null;
        reference.Stops.Add(new DeliveryRouteStop { StopSequence = 1, Order = new Order { Status = OrderStatus.Delivered,
            StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ready = Start.AddMinutes(-5), ontheway = Start.AddMinutes(5), delivered = Start.AddMinutes(30) }) } });
        reference.Stops.Add(new DeliveryRouteStop { StopSequence = 2, Order = new Order { Status = OrderStatus.Delivered,
            StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ready = Start.AddMinutes(-10), ontheway = Start, delivered = Start.AddMinutes(20) }) } });
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(observed, new([], 0, 0, false)), new(reference, new([], 0, 0, false))]);
        Assert.Equal(2, summary.StageSampleCount);
        Assert.Equal(7.5d, summary.AvgDispatchMinutes);
        Assert.Equal(25d, summary.AvgDeliveryStageMinutes);
        Assert.Equal(15d, summary.AvgReturnMinutes);
        Assert.Null(reference.DepartedAtUtc);
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(5, 20)]
    [InlineData(-5, 40)]
    public void AssignedDepartureDoesNotInventMissingOrInconsistentStageTimes(int? readyMinutes, int deliveredMinutes)
    {
        var route = Route(minutes: 30);
        route.DepartedAtUtc = null;
        route.Stops.Add(new DeliveryRouteStop { Order = new Order { Status = OrderStatus.Delivered,
            StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ready = readyMinutes.HasValue ? (DateTime?)Start.AddMinutes(readyMinutes.Value) : null,
                ontheway = Start, delivered = Start.AddMinutes(deliveredMinutes) }) } });
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(route, new([], 0, 0, false))]);
        Assert.Equal(0, summary.StageSampleCount);
        Assert.Null(summary.AvgDispatchMinutes);
        Assert.Null(summary.AvgDeliveryStageMinutes);
        Assert.Null(summary.AvgReturnMinutes);
    }

    [Fact]
    public void AssignedDeparturePreservesZeroLengthStages()
    {
        var route = Route(minutes: 0);
        route.DepartedAtUtc = null;
        route.Stops.Add(new DeliveryRouteStop { Order = new Order { Status = OrderStatus.Delivered,
            StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ready = Start, ontheway = Start, delivered = Start }) } });
        var summary = Summarize<DashboardDeliveryPerformanceSummaryDto>([], [new(route, new([], 0, 0, false))]);
        Assert.Equal(1, summary.StageSampleCount);
        Assert.Equal(0d, summary.AvgDispatchMinutes);
        Assert.Equal(0d, summary.AvgDeliveryStageMinutes);
        Assert.Equal(0d, summary.AvgReturnMinutes);
    }

    [Fact]
    public void GpsGapsAreSeparatedAndNeverBecomeCompleteDistance()
    {
        var route = Route(minutes: 10);
        var track = AnalyzeTrack(route, [Point(0), Point(60, -75.579m), Point(540, -75.575m), Point(600, -75.574m)]);
        Assert.Equal(2, track.Segments.Count);
        Assert.Equal(20d, track.CoveragePercent);
        Assert.False(track.Complete);
    }

    [Fact]
    public void UnreliablePointsAndOtherSessionsCannotConnectSegments()
    {
        var bad = Point(60); bad.AccuracyMeters = 200;
        var other = Point(90); other.WorkSessionId = 99;
        var track = AnalyzeTrack(Route(minutes: 2), [Point(0), bad, other, Point(120, -75.579m)]);
        Assert.Equal(2, track.Segments.Count);
        Assert.Equal(2, track.Segments.Sum(s => s.Count));
        Assert.Equal(0d, track.DistanceKm);
        Assert.False(track.Complete);
    }

    [Fact]
    public void GoodTrackUsesRecordedTimeAndIgnoresPlannedDistance()
    {
        var route = Route(minutes: 2); route.PlannedDistanceMeters = 100000;
        var track = AnalyzeTrack(route, [Point(120, -75.578m), Point(0), Point(60, -75.579m)]);
        Assert.True(track.Complete);
        Assert.Equal(100d, track.CoveragePercent);
        Assert.InRange(track.DistanceKm, .2, .3);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"delivered\":null}")]
    [InlineData("{\"delivered\":\"invalid\"}")]
    public void InvalidDeliveryTimestampStaysUnknown(string json) => Assert.Null(StatusTime(json, "delivered"));

    [Fact]
    public async Task DeliveryDateAndBranchScopeSurviveAnEditAfterTheReportPeriod()
    {
        await using var db = TestDatabase();
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Sucursal" };
        var driver = new User { Id = 7, TenantId = 1, BranchId = 1, Branch = branch, Role = UserRole.Deliveryman, Name = "Domiciliario" };
        var order = new Order { Id = 1, TenantId = 1, BranchId = 1, Branch = branch, DeliveryManId = 7, DeliveryMan = driver,
            Type = OrderType.Delivery, Status = OrderStatus.Delivered, CreatedAt = Start.AddDays(-1), UpdatedAt = Start.AddDays(3),
            StatusTimes = "{\"delivered\":\"2026-10-22T17:05:00Z\"}", Total = 20000 };
        db.Add(order);
        await db.SaveChangesAsync();
        var clock = new Mock<IClock>();
        var repository = new OrderRepository(db, clock.Object);
        var orders = await repository.GetDeliveredDeliveryOrdersForDashboardAsync(1, Start, Start.AddHours(1));
        Assert.Single(orders);
        var ticks = await repository.GetDeliveredOrdersSalesTicksForDashboardAsync(1, Start, Start.AddHours(1));
        Assert.Equal(Start.AddMinutes(5), Assert.Single(ticks).UpdatedAt);
        var performance = await BuildAsync(db, 1, null, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Equal(1, performance.Current.DeliveredCount);
        Assert.Null(performance.Current.AvgOrderMinutes);
        var otherBranch = await BuildAsync(db, 2, null, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Equal(0, otherBranch.Current.DeliveredCount);
    }

    [Fact]
    public async Task SelfAnalyticsCannotExposeTeamDriverOptions()
    {
        await using var db = TestDatabase();
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Sucursal" };
        foreach (var id in new[] { 7, 8 })
        {
            var driver = new User { Id = id, TenantId = 1, BranchId = 1, Branch = branch, Role = UserRole.Deliveryman, Name = $"D{id}" };
            db.Orders.Add(new Order { Id = id, TenantId = 1, BranchId = 1, Branch = branch, DeliveryManId = id, DeliveryMan = driver,
                Type = OrderType.Delivery, Status = OrderStatus.Delivered, CreatedAt = Start.AddDays(-1), UpdatedAt = Start,
                StatusTimes = "{\"delivered\":\"2026-10-22T17:00:00Z\"}" });
        }
        await db.SaveChangesAsync();
        var own = await BuildAsync(db, 1, 7, Start, Start.AddHours(1), CancellationToken.None, ownOnly: true);
        Assert.Equal(7, Assert.Single(own.DriverOptions).Id);
        Assert.Equal(7, Assert.Single(own.Drivers).Id);
        Assert.Equal(1, own.Current.DeliveredCount);
        Assert.Null(own.Team);
        Assert.Empty(own.TeamEvolution);
        var team = await BuildAsync(db, 1, 7, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Equal(2, team.DriverOptions.Count);
        Assert.Equal(1, team.Current.DeliveredCount);
        Assert.Equal(1, team.Team!.DeliveredCount);
        Assert.Equal(1, team.TeamEvolution.Sum(bucket => bucket.DeliveredCount));
        var all = await BuildAsync(db, 1, null, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Null(all.Team);
        Assert.Empty(all.TeamEvolution);
    }

    [Fact]
    public async Task IndividualBenchmarkExcludesSelectedDriverAndWeightsOtherDriversWithinTheBranch()
    {
        await using var db = TestDatabase();
        var branches = new[] { new Branch { Id = 1, TenantId = 1, Name = "Sucursal" }, new Branch { Id = 2, TenantId = 1, Name = "Otra" } };
        var drivers = new[] { 7, 8, 9, 10 }.ToDictionary(id => id, id => new User { Id = id, TenantId = 1,
            BranchId = id == 9 ? 2 : 1, Branch = branches[id == 9 ? 1 : 0], Role = UserRole.Deliveryman, Name = $"D{id}" });
        var ids = new[] { 7, 8, 8, 8, 10, 9 };
        for (var i = 0; i < ids.Length; i++)
        {
            var driver = drivers[ids[i]];
            var minutes = driver.Id == 8 ? 30 : 10;
            var delivered = Start.AddMinutes(minutes / 2);
            var order = new Order { Id = i + 1, TenantId = 1, BranchId = driver.BranchId, Branch = driver.Branch,
                DeliveryManId = driver.Id, DeliveryMan = driver, Type = OrderType.Delivery, Status = OrderStatus.Delivered,
                CreatedAt = Start.AddDays(-1), UpdatedAt = delivered, StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ontheway = Start, delivered }) };
            var route = Route(i + 1, minutes);
            route.TenantId = 1; route.BranchId = driver.BranchId; route.Branch = driver.Branch;
            route.DeliverymanId = driver.Id; route.Deliveryman = driver; route.MetSla = driver.Id != 8;
            route.Stops.Add(new DeliveryRouteStop { Id = i + 1, TenantId = 1, OrderId = order.Id, Order = order, DeliveryRouteId = route.Id });
            db.DeliveryRoutes.Add(route);
            for (var minute = 0; minute <= minutes; minute++)
                db.DeliverymanLocations.Add(new DeliverymanLocation { TenantId = 1, DeliveryRouteId = route.Id,
                    DeliverymanId = driver.Id, WorkSessionId = 3, RecordedAt = Start.AddMinutes(minute),
                    Latitude = 6.24m, Longitude = -75.58m + minute * (driver.Id == 7 ? .002m : .001m), AccuracyMeters = 10, GpsEnabled = true });
        }
        await db.SaveChangesAsync();
        var selected = await BuildAsync(db, 1, 7, Start, Start.AddHours(1), CancellationToken.None);
        var all = await BuildAsync(db, 1, null, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Equal(10d, selected.Current.AvgRouteMinutes);
        Assert.Equal(100d, selected.Current.OnTimePercent);
        Assert.Equal(1, selected.Current.GpsSampleCount);
        Assert.Equal(25d, selected.Team!.AvgRouteMinutes);
        Assert.Equal(12.5d, selected.Team.AvgOrderMinutes);
        Assert.Equal(25d, selected.Team.OnTimePercent);
        Assert.Equal(4, selected.Team.GpsSampleCount);
        Assert.NotEqual(all.Current.MinutesPerKm, selected.Team.MinutesPerKm);
        Assert.Equal(25d, Assert.Single(selected.TeamEvolution, x => x.RouteSampleCount > 0).AvgRouteMinutes);
        var other = await BuildAsync(db, 1, 8, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Equal(10d, other.Team!.AvgRouteMinutes);
        Assert.Equal(100d, other.Team.OnTimePercent);
        Assert.Equal(2, other.Team.GpsSampleCount);
    }

    [Fact]
    public async Task OrderTimingUsesOwnStatusHistoryEvenWithoutARoute()
    {
        await using var db = TestDatabase();
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Sucursal" };
        var driver = new User { Id = 7, TenantId = 1, BranchId = 1, Branch = branch, Role = UserRole.Deliveryman, Name = "D7" };
        var orders = new List<Order>();
        foreach (var minutes in new[] { 5, 25, 30 })
        {
            var delivered = Start.AddMinutes(minutes);
            var ontheway = minutes == 30 ? Start.AddMinutes(40) : Start;
            var order = new Order { Id = minutes, TenantId = 1, BranchId = 1, Branch = branch, DeliveryManId = 7, DeliveryMan = driver,
                Type = OrderType.Delivery, Status = OrderStatus.Delivered, CreatedAt = Start.AddDays(-1), UpdatedAt = delivered,
                StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { ontheway, delivered }) };
            orders.Add(order);
            db.Orders.Add(order);
        }
        var route = Route(minutes: 45);
        route.TenantId = 1; route.BranchId = 1; route.Branch = branch; route.Deliveryman = driver;
        route.DepartedAtUtc = Start.AddMinutes(10);
        route.Stops.Add(new DeliveryRouteStop { Id = 1, TenantId = 1, OrderId = 25, Order = orders[1], DeliveryRouteId = 1 });
        db.DeliveryRoutes.Add(route);
        await db.SaveChangesAsync();
        var result = await BuildAsync(db, 1, 7, Start, Start.AddHours(1), CancellationToken.None);
        Assert.Equal(3, result.Current.DeliveredCount);
        Assert.Equal(2, result.Current.OrderSampleCount);
        Assert.Equal(15d, result.Current.AvgOrderMinutes);
        Assert.Equal(25d, result.Current.P90OrderMinutes);
        Assert.Equal(35d, result.Current.AvgRouteMinutes);
        Assert.Equal(0, result.Team!.CompletedRoutesCount);
        Assert.Null(result.Team.AvgRouteMinutes);
        Assert.Null(result.Team.AvgOrderMinutes);
        Assert.Null(result.Team.MinutesPerKm);
        Assert.Null(result.Team.OnTimePercent);
        Assert.All(result.TeamEvolution, bucket => Assert.Null(bucket.AvgOrderMinutes));
    }

    private static ApplicationDbContext TestDatabase()
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(t => t.TenantId).Returns(1);
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: tenant.Object);
    }

    [Fact]
    public async Task RouteDetailsExposeEachOrdersOnTheWayTimeWithoutReplacingMissingDeparture()
    {
        await using var db = TestDatabase();
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Sucursal" };
        var driver = new User { Id = 7, TenantId = 1, BranchId = 1, Branch = branch, Role = UserRole.Deliveryman, Name = "Domiciliario" };
        var route = new DeliveryRoute { Id = 1, TenantId = 1, BranchId = 1, Branch = branch, DeliverymanId = 7, Deliveryman = driver,
            Status = DeliveryRouteStatus.Completed, CompletedAtUtc = Start.AddMinutes(40), RouteStartedAtUtc = Start.AddMinutes(3) };
        foreach (var id in new[] { 1, 2, 3 })
        {
            var order = new Order { Id = id, TenantId = 1, BranchId = 1, Branch = branch, DeliveryManId = 7, DeliveryMan = driver,
                Type = OrderType.Delivery, Status = OrderStatus.Delivered,
                StatusTimes = id == 3 ? "{\"ontheway\":\"invalid\"}" : System.Text.Json.JsonSerializer.Serialize(new { ontheway = Start.AddSeconds(id), delivered = Start.AddMinutes(id * 10) }) };
            route.Stops.Add(new DeliveryRouteStop { Id = id, TenantId = 1, OrderId = id, Order = order, DeliveryRouteId = 1, StopSequence = id });
        }
        db.DeliveryRoutes.Add(route);
        await db.SaveChangesAsync();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.Role).Returns("Superadmin");
        var handler = new GetDashboardDeliveryRouteStopsHandler(db, currentUser.Object);
        var result = await handler.Handle(new GetDashboardDeliveryRouteStopsQuery { RouteId = 1, BranchId = 1 }, CancellationToken.None);
        Assert.Null(result.DepartedAtUtc);
        Assert.Empty(result.Segments);
        Assert.Equal(Start.AddSeconds(1), result.Stops[0].OnTheWayAtUtc);
        Assert.Equal(Start.AddSeconds(2), result.Stops[1].OnTheWayAtUtc);
        Assert.Equal(Start.AddMinutes(20), result.Stops[1].DeliveredAtUtc);
        Assert.Null(result.Stops[2].OnTheWayAtUtc);
        Assert.Null(result.Stops[2].DeliveredAtUtc);
    }

    [Theory]
    [InlineData("2026-10-21T04:30:00Z", true)]
    [InlineData("2026-10-22T04:30:00Z", false)]
    [InlineData("2026-10-22T05:00:00Z", true)]
    public void WednesdayExclusionUsesColombiaCalendar(string utc, bool included)
    {
        var instant = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(included, IncludesDay(instant, true));
        Assert.True(IncludesDay(instant, false));
    }

    [Fact]
    public void DailyBucketsRemoveWednesdaysWithoutAddingAnExtraUtcDay()
    {
        var from = new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 24, 4, 59, 59, DateTimeKind.Utc);
        var all = SenorArroz.Application.Features.Dashboard.Services.DashboardDeliveryTimeBuckets.Create(from, to);
        var filtered = SenorArroz.Application.Features.Dashboard.Services.DashboardDeliveryTimeBuckets.Create(from, to, true);
        Assert.Equal(23, all.Count);
        Assert.Equal(20, filtered.Count);
        Assert.DoesNotContain(filtered.Labels, label => label.Contains("mié"));
        Assert.Equal(-1, filtered.GetBucketIndex(new DateTime(2026, 10, 22, 4, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(filtered.Count - 1, filtered.GetBucketIndex(to));
    }

    [Fact]
    public void HourlyBucketsRemainWithinTheSameColombiaDay()
    {
        var from = new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1).AddTicks(-1);
        var buckets = SenorArroz.Application.Features.Dashboard.Services.DashboardDeliveryTimeBuckets.Create(from, to);
        Assert.Equal(24, buckets.Count);
        Assert.Equal(21, buckets.GetBucketIndex(new DateTime(2026, 10, 23, 2, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task ExcludingWednesdayRecalculatesBothPeriodsAndRouteHistory()
    {
        await using var db = TestDatabase();
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Sucursal" };
        var driver = new User { Id = 7, TenantId = 1, BranchId = 1, Branch = branch, Role = UserRole.Deliveryman, Name = "Domiciliario" };
        foreach (var day in new[] { 14, 15, 21, 22 })
        {
            var delivered = new DateTime(2026, 10, day, 17, 0, 0, DateTimeKind.Utc);
            var order = new Order { Id = day, TenantId = 1, BranchId = 1, Branch = branch, DeliveryManId = 7, DeliveryMan = driver,
                Type = OrderType.Delivery, Status = OrderStatus.Delivered, CreatedAt = delivered.AddDays(-1), UpdatedAt = delivered,
                StatusTimes = System.Text.Json.JsonSerializer.Serialize(new { delivered }) };
            var route = new DeliveryRoute { Id = day, TenantId = 1, BranchId = 1, Branch = branch, DeliverymanId = 7, Deliveryman = driver,
                Status = DeliveryRouteStatus.Completed, CompletedAtUtc = delivered.AddMinutes(15), DepartedAtUtc = delivered.AddMinutes(-15), MetSla = true };
            route.Stops.Add(new DeliveryRouteStop { Id = day, TenantId = 1, OrderId = day, Order = order, DeliveryRouteId = day });
            db.DeliveryRoutes.Add(route);
        }
        await db.SaveChangesAsync();
        var from = new DateTime(2026, 10, 19, 5, 0, 0, DateTimeKind.Utc);
        var filtered = await BuildAsync(db, 1, null, from, from.AddDays(7).AddTicks(-1), CancellationToken.None, excludeWednesdays: true);
        Assert.Equal(1, filtered.Current.DeliveredCount);
        Assert.Equal(1, filtered.Previous.DeliveredCount);
        Assert.Equal(1, filtered.Current.CompletedRoutesCount);
        Assert.Equal(1, filtered.Previous.CompletedRoutesCount);
        Assert.Equal(22, Assert.Single(filtered.RecentRoutes).Id);
        Assert.Equal(1, filtered.Evolution.Sum(bucket => bucket.DeliveredCount));
        Assert.DoesNotContain(filtered.Labels, label => label.Contains("mié"));
    }
}
