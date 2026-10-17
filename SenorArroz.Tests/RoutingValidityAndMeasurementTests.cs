using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class RoutingValidityAndMeasurementTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MeasurementIsDisabledByDefaultAndExpiresWithoutRestartingItsClock()
    {
        var options = new RoutingMeasurementOptions();
        Assert.False(options.IsActive(Now));
        options.Enabled = true;
        Assert.False(options.IsActive(Now));
        options.StartedAtUtc = new DateTimeOffset(Now);
        options.ExpiresAtUtc = options.StartedAtUtc.Value.AddDays(14);
        Assert.False(options.IsActive(Now.AddTicks(-1)));
        Assert.True(options.IsActive(Now));
        Assert.True(options.IsActive(Now.AddDays(14).AddTicks(-1)));
        Assert.False(options.IsActive(Now.AddDays(14)));
        options.ExpiresAtUtc = options.StartedAtUtc.Value.AddDays(15);
        Assert.False(options.IsActive(Now));
    }

    [Fact]
    public async Task FutureReservationsAreExcludedFromPlanAndPreviewButReadyFoodIsNotHidden()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.Taken, reservation: true, prepareAt: Now.AddDays(1));
        await f.AddOrder(21, OrderStatus.Ready, reservation: true, prepareAt: Now.AddDays(1));
        var plan = await f.Service.GetOrCreateActivePlanAsync(1);
        Assert.Equal(new[] { 21 }, plan.Proposals.SelectMany(p => p.Stops).Select(s => s.OrderId));
        Assert.DoesNotContain(plan.UnroutedOrders, s => s.OrderId == 20);
        await Assert.ThrowsAsync<BusinessException>(() => f.Service.PreviewAsync(1, new[] { 20 }));
    }

    [Fact]
    public async Task PendingOnlyRouteCannotRecommendLeaveNowAndTimelineStartsAtDepot()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.InPreparation);
        f.ReadyInSeconds = 120;
        var plan = await f.Service.GetOrCreateActivePlanAsync(1);
        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal("wait", proposal.Recommendation);
        Assert.False(proposal.IsClaimable);
        Assert.Equal(Now.AddSeconds(120), proposal.ExpectedDepartureAtUtc);
        Assert.All(proposal.Stops, s => Assert.True(s.EstimatedArrivalAtUtc >= proposal.ExpectedDepartureAtUtc));
        var preview = await f.Service.PreviewAsync(1, new[] { 20 });
        Assert.Equal(proposal.ExpectedDepartureAtUtc, preview.ExpectedDepartureAtUtc);
        Assert.Equal(proposal.Stops[0].EstimatedArrivalAtUtc, preview.Stops[0].EstimatedArrivalAtUtc);
    }

    [Fact]
    public async Task LongPreparationDoesNotPreventTheReadySubsetFromLeaving()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.Ready);
        await f.AddOrder(21, OrderStatus.InPreparation);
        f.ReadyInSeconds = 600;
        var plan = await f.Service.GetOrCreateActivePlanAsync(1);
        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal("leaveNow", proposal.Recommendation);
        Assert.Equal(new[] { 20 }, proposal.ClaimableReadyOrderIds);
        Assert.Equal("waitingWouldDelayReadyOrders", Assert.Single(plan.UnroutedOrders).UnroutedReason);
    }

    [Fact]
    public async Task ReuseSkipsSolverButChangedCourierEtaAndExpirationRecalculate()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.Ready);
        var first = await f.Service.GetOrCreateActivePlanAsync(1);
        f.Clock.UtcNow = Now.AddSeconds(10);
        Assert.Equal(first.Id, (await f.Service.GetOrCreateActivePlanAsync(1)).Id);
        Assert.Equal(1, f.SolverCalls);
        f.AvailableInSeconds = 240;
        var next = await f.Service.GetOrCreateActivePlanAsync(1);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal("next", Assert.Single(next.Proposals).Recommendation);
        f.Clock.UtcNow = Now.AddMinutes(5);
        Assert.NotEqual(next.Id, (await f.Service.GetOrCreateActivePlanAsync(1)).Id);
        Assert.Equal(3, f.SolverCalls);
    }

    [Fact]
    public async Task OrderChangedDuringSolveIsNotPublishedAsAnAvailableRoute()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.Ready);
        f.DuringSolve = () =>
        {
            f.Db.Orders.Single().Status = OrderStatus.Delivered;
            f.Db.SaveChanges();
        };
        await Assert.ThrowsAsync<RoutingPlanStaleException>(() => f.Service.GetOrCreateActivePlanAsync(1));
        Assert.Empty(await f.Db.DeliveryRoutingPlans.ToListAsync());
    }

    [Fact]
    public async Task RecalculationCannotRestartTheReadyFoodWaitingDeadline()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.Ready);
        var order = f.Db.Orders.Single();
        order.AddStatusTime(OrderStatus.Ready, Now.AddSeconds(-60));
        await f.Db.SaveChangesAsync();
        var first = await f.Service.GetOrCreateActivePlanAsync(1);
        f.Clock.UtcNow = Now.AddSeconds(30);
        var next = await f.Service.RecalculateAsync(1);
        Assert.Equal(Now.AddSeconds(120), first.Proposals.Single().WaitDeadlineAtUtc);
        Assert.Equal(first.Proposals.Single().WaitDeadlineAtUtc, next.Proposals.Single().WaitDeadlineAtUtc);
    }

    [Fact]
    public async Task SolverFailureProducesAnExplicitManualFallback()
    {
        await using var f = new Fixture();
        await f.AddOrder(20, OrderStatus.Ready);
        f.DuringSolve = () => throw new InvalidOperationException("Unavailable solver");
        var plan = await f.Service.GetOrCreateActivePlanAsync(1);
        Assert.Empty(plan.Proposals);
        Assert.Single(plan.UnroutedOrders);
        Assert.Contains("selección manual", plan.Warnings);
    }

    [Fact]
    public void OptimizerSkipsOutOfDomainNodesAndVehiclesInsteadOfThrowingNativeSetMin()
    {
        var options = Options.Create(new DeliveryRoutingOptions { SolverTimeLimitMs = 10 });
        var matrix = new ApproximateRoutingCostMatrixProvider(options).Create(6.2, -75.5,
        [new RoutingNode(20, 6.201, -75.501, Now, Now.AddDays(3), false, 60)]);
        var optimizer = new OrToolsDeliveryRouteOptimizer(options);
        var futureOrder = optimizer.Optimize(new DeliveryRouteOptimizationRequest(matrix, [new RoutingVehicleSlot(0)], Now));
        Assert.Empty(futureOrder.Routes);
        Assert.Equal(new[] { 0 }, futureOrder.UnroutedNodeIndexes);
        var futureDriver = optimizer.Optimize(new DeliveryRouteOptimizationRequest(matrix, [new RoutingVehicleSlot(300000)], Now));
        Assert.Empty(futureDriver.Routes);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly ApplicationDbContext Db = new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        internal readonly FakeClock Clock = new(Now);
        internal readonly DeliveryRoutingPlanService Service;
        internal int SolverCalls;
        internal int ReadyInSeconds;
        internal int AvailableInSeconds;
        internal Action? DuringSolve;

        internal Fixture()
        {
            Db.Branches.Add(new Branch { Id = 1, Name = "Branch", Address = "Store", Phone1 = "3000000000", Latitude = 6.2m, Longitude = -75.5m });
            var options = Options.Create(new DeliveryRoutingOptions { MaximumFinalistsToValidate = 0 });
            var estimator = new Mock<IKitchenPreparationEstimator>();
            estimator.Setup(e => e.EstimateAsync(1, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns((int branch, IReadOnlyCollection<int> ids, DateTime now, CancellationToken ct) =>
                    Task.FromResult<IReadOnlyDictionary<int, KitchenPreparationEstimate>>(ids.ToDictionary(id => id,
                        id => new KitchenPreparationEstimate(now.AddSeconds(ReadyInSeconds), "low"))));
            var capacity = new Mock<IDeliverymanAvailabilityService>();
            capacity.Setup(c => c.GetCapacityAsync(1, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new DeliveryRoutingCapacity([new RoutingVehicleSlot(AvailableInSeconds)], AvailableInSeconds == 0 ? 1 : 0, AvailableInSeconds > 0 ? 1 : 0, []));
            var optimizer = new Mock<IDeliveryRouteOptimizer>();
            optimizer.Setup(o => o.Optimize(It.IsAny<DeliveryRouteOptimizationRequest>())).Returns((DeliveryRouteOptimizationRequest request) =>
            {
                SolverCalls++;
                DuringSolve?.Invoke();
                return new DeliveryRouteOptimizationResult([new OptimizedRoute(0, Enumerable.Range(0, request.Matrix.Nodes.Count).ToArray(), 100, 100, 1)], [], 1, []);
            });
            Service = new DeliveryRoutingPlanService(Db, Clock, new ApproximateRoutingCostMatrixProvider(options), optimizer.Object,
                estimator.Object, capacity.Object, Mock.Of<IGoogleRoutesDrivingMetricsService>(), Mock.Of<IOrderNotificationService>(),
                options, Mock.Of<ILogger<DeliveryRoutingPlanService>>());
        }

        internal async Task AddOrder(int id, OrderStatus status, bool reservation = false, DateTime? prepareAt = null)
        {
            Db.Addresses.Add(new Address { Id = id, CustomerId = id, AddressText = "Address", Latitude = 6.201m, Longitude = -75.501m });
            Db.Orders.Add(new Order { Id = id, BranchId = 1, TakenById = 1, AddressId = id,
                Type = reservation ? OrderType.Reservation : OrderType.Delivery, Status = status, PrepareAt = prepareAt, CreatedAt = Now.AddMinutes(-20) });
            await Db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
