using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class DeliveryRoutingPlanServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPlan_ReusesActivePlan_WhenOperationalInputsHaveNotChanged()
    {
        await using var db = CreateContext();
        db.Branches.Add(CreateBranch());
        await db.SaveChangesAsync();
        var clock = new FakeClock(Now);
        var notifications = new Mock<IOrderNotificationService>();
        var service = CreateService(db, clock, notifications);

        var first = await service.GetOrCreateActivePlanAsync(1);
        clock.UtcNow = Now.AddMinutes(1);
        var second = await service.GetOrCreateActivePlanAsync(1);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Version, second.Version);
        Assert.Single(await db.DeliveryRoutingPlans.ToListAsync());
        notifications.Verify(x => x.NotifyDeliveryRoutingPlanChanged(1, first.Id, first.Version), Times.Once);
    }

    [Fact]
    public async Task GetPlan_ReusesActivePlan_WhenReadyEstimateMovesWithCurrentTime()
    {
        await using var db = CreateContext();
        db.Branches.Add(CreateBranch());
        var address = new Address
        {
            Id = 10,
            CustomerId = 10,
            NeighborhoodId = 10,
            AddressText = "Calle 10 # 20-30",
            Latitude = 7.1254m,
            Longitude = -73.1198m,
        };
        db.Addresses.Add(address);
        db.Orders.Add(new Order
        {
            Id = 20,
            BranchId = 1,
            TakenById = 1,
            AddressId = address.Id,
            Address = address,
            Type = OrderType.Delivery,
            Status = OrderStatus.Ready,
            CreatedAt = Now.AddMinutes(-20),
        });
        await db.SaveChangesAsync();
        var clock = new FakeClock(Now);
        var notifications = new Mock<IOrderNotificationService>();
        var service = CreateService(db, clock, notifications);

        var first = await service.GetOrCreateActivePlanAsync(1);
        clock.UtcNow = Now.AddSeconds(5);
        var second = await service.GetOrCreateActivePlanAsync(1);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Version, second.Version);
        Assert.Single(await db.DeliveryRoutingPlans.ToListAsync());
        notifications.Verify(x => x.NotifyDeliveryRoutingPlanChanged(1, first.Id, first.Version), Times.Once);
    }

    [Fact]
    public async Task GetPlan_CreatesNewVersion_WhenOperationalInputsChange()
    {
        await using var db = CreateContext();
        db.Branches.Add(CreateBranch());
        await db.SaveChangesAsync();
        var clock = new FakeClock(Now);
        var notifications = new Mock<IOrderNotificationService>();
        var service = CreateService(db, clock, notifications);
        var first = await service.GetOrCreateActivePlanAsync(1);
        db.Orders.Add(new Order
        {
            Id = 20,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.Taken,
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var second = await service.GetOrCreateActivePlanAsync(1);

        Assert.Equal(first.Version + 1, second.Version);
        Assert.Equal(2, await db.DeliveryRoutingPlans.CountAsync());
        Assert.Equal(DeliveryRoutingPlanStatus.Superseded,
            await db.DeliveryRoutingPlans.Where(x => x.Id == first.Id).Select(x => x.Status).SingleAsync());
        notifications.Verify(x => x.NotifyDeliveryRoutingPlanChanged(1, It.IsAny<int>(), It.IsAny<long>()), Times.Exactly(2));
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Branch CreateBranch() => new()
    {
        Id = 1,
        Name = "Principal",
        Address = "Sucursal",
        Phone1 = "3000000000",
    };

    private static DeliveryRoutingPlanService CreateService(
        ApplicationDbContext db,
        FakeClock clock,
        Mock<IOrderNotificationService> notifications)
    {
        var estimator = new Mock<IKitchenPreparationEstimator>();
        estimator
            .Setup(x => x.EstimateAsync(
                It.IsAny<int>(),
                It.IsAny<IReadOnlyCollection<int>>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns((int _, IReadOnlyCollection<int> orderIds, DateTime nowUtc, CancellationToken _) =>
                Task.FromResult<IReadOnlyDictionary<int, KitchenPreparationEstimate>>(
                    orderIds.ToDictionary(id => id, _ => new KitchenPreparationEstimate(nowUtc, "low"))));

        return new DeliveryRoutingPlanService(
            db,
            clock,
            Mock.Of<IRoutingCostMatrixProvider>(),
            Mock.Of<IDeliveryRouteOptimizer>(),
            estimator.Object,
            Mock.Of<IDeliverymanAvailabilityService>(),
            Mock.Of<IGoogleRoutesDrivingMetricsService>(),
            notifications.Object,
            Options.Create(new DeliveryRoutingOptions()),
            Mock.Of<ILogger<DeliveryRoutingPlanService>>());
    }
}
