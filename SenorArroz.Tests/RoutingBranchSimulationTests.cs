using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class RoutingBranchSimulationTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 16)]
    public async Task MultipleZonesRespectFleetPreparationAndBranchBoundaries(int couriers, int orders)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var now = DateTime.UtcNow;
        db.Branches.Add(new Branch { Id = 1, Name = "Branch", Address = "Store", Phone1 = "3000000000",
            Latitude = 6m, Longitude = -75m, DeliveryTrackingAllowedDistanceMeters = 50 });
        for (var id = 1; id <= couriers; id++)
        {
            db.Users.Add(new User { Id = id, BranchId = 1, Name = $"Courier {id}", Email = $"{id}@test",
                PasswordHash = "test", Role = UserRole.Deliveryman, Active = true });
            db.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = id, DeliverymanId = id, BranchId = 1,
                StartedAt = now.AddHours(-1), AutoCloseAt = now.AddHours(6), LastCommunicationAt = now,
                Status = DeliveryWorkSessionStatus.Active, DeviceInstallationId = $"device-{id}", DevicePlatform = "android" });
            db.DeliverymanLocations.Add(new DeliverymanLocation { DeliverymanId = id, WorkSessionId = id,
                ClientPointId = Guid.NewGuid(), RecordedAt = now, Latitude = 6m, Longitude = -75m,
                AccuracyMeters = 10, GpsEnabled = true, TrackingMode = DeliveryTrackingMode.Light });
        }
        for (var id = 1; id <= orders; id++)
        {
            db.Addresses.Add(new Address { Id = id, CustomerId = 1, AddressText = "Destination",
                Latitude = 6m + (id % 2 == 0 ? .005m : -.005m), Longitude = -75m + id * .0002m });
            var order = new Order { Id = id, BranchId = 1, TakenById = 1, AddressId = id,
                Type = OrderType.Delivery, Status = id % 4 == 0 ? OrderStatus.InPreparation : OrderStatus.Ready,
                CreatedAt = now.AddMinutes(-20) };
            if (order.Status == OrderStatus.Ready) order.AddStatusTime(OrderStatus.Ready, now.AddSeconds(-10));
            db.Orders.Add(order);
        }
        db.Orders.Add(new Order { Id = 1000, BranchId = 2, TakenById = 1, Type = OrderType.Delivery, Status = OrderStatus.Ready });
        db.Orders.Add(new Order { Id = 1001, BranchId = 1, TakenById = 1, Type = OrderType.Reservation,
            AddressId = 1, PrepareAt = now.AddDays(1), Status = OrderStatus.Taken });
        await db.SaveChangesAsync();
        var settings = Options.Create(new DeliveryRoutingOptions { MaximumFinalistsToValidate = 0, SolverTimeLimitMs = 250 });
        var service = new DeliveryRoutingPlanService(db, new FakeClock(now), new ApproximateRoutingCostMatrixProvider(settings),
            new OrToolsDeliveryRouteOptimizer(settings), new KitchenPreparationEstimator(db, settings),
            new DeliverymanAvailabilityService(db, Mock.Of<IGoogleRoutesDrivingMetricsService>(), settings),
            Mock.Of<IGoogleRoutesDrivingMetricsService>(), Mock.Of<IOrderNotificationService>(), settings,
            NullLogger<DeliveryRoutingPlanService>.Instance);
        var plan = await service.GetOrCreateActivePlanAsync(1);
        Assert.Equal(couriers, plan.Capacity.AvailableNow);
        Assert.InRange(plan.Proposals.Count, 1, couriers);
        var proposed = plan.Proposals.SelectMany(p => p.Stops).Select(s => s.OrderId).ToArray();
        Assert.Equal(proposed.Length, proposed.Distinct().Count());
        Assert.Equal(orders, proposed.Length + plan.UnroutedOrders.Count);
        Assert.DoesNotContain(1000, proposed);
        Assert.DoesNotContain(1001, proposed);
        foreach (var route in plan.Proposals)
        {
            Assert.All(route.Stops, stop => Assert.True(route.ExpectedDepartureAtUtc >= stop.EstimatedReadyAtUtc));
            Assert.Equal((int)(route.Stops.Max(stop=>stop.EstimatedArrivalAtUtc!.Value)-route.ExpectedDepartureAtUtc).TotalSeconds,
                route.LastDeliverySeconds);
            Assert.InRange(route.LastDeliverySeconds,0,SenorArroz.Application.Common.Helpers.RoutingScheduleHelper.NormalLimit(settings.Value));
            if (route.Stops.Any(s => s.IsReady)) Assert.All(route.Stops, stop => Assert.True(stop.IsReady));
        }
        Assert.Equal(0, await db.Orders.CountAsync(o => o.DeliveryManId.HasValue));
    }
}
