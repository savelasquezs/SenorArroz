using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class RoutingProposalAcceptancePostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task EditedSelectionPreservesOrderAndOnlyOneConcurrentCourierCanClaimIt()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var now = DateTime.UtcNow;
        var clock = new FakeClock(now);
        await using var seed = CreateDb();
        await seed.Database.EnsureCreatedAsync();
        seed.Tenants.Add(new Tenant { Id = 1, Name = "Test", Slug = "test" });
        seed.Branches.Add(new Branch { Id = 1, Name = "Test", Address = "Store", Phone1 = "3000000000", Latitude = 6m, Longitude = -75m });
        foreach (var id in new[] { 24, 25 })
        {
            seed.Users.Add(new User { Id = id, BranchId = 1, Name = $"Courier {id}", Email = $"{id}@test.local",
                PasswordHash = "test", Role = UserRole.Deliveryman, Active = true });
            seed.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = id, BranchId = 1, DeliverymanId = id,
                StartedAt = now.AddHours(-1), AutoCloseAt = now.AddHours(6), Status = DeliveryWorkSessionStatus.Active,
                LastCommunicationAt = now, DeviceInstallationId = $"device-{id}", DevicePlatform = "android" });
        }
        seed.Customers.Add(new Customer { Id = 1, BranchId = 1, Name = "Test", Phone1 = "3000000001" });
        seed.Addresses.Add(new Address { Id = 1, CustomerId = 1, AddressText = "Test", Latitude = 6.001m, Longitude = -75m });
        for (var id = 1; id <= 3; id++) seed.Orders.Add(new Order { Id = id, BranchId = 1, TakenById = 24,
            AddressId = 1, Type = OrderType.Delivery, Status = OrderStatus.Ready });
        var plan = new DeliveryRoutingPlan { BranchId = 1, GenerationNumber = 1, GeneratedAtUtc = now,
            InputFingerprint = "test", Proposals = [new DeliveryRouteProposal { Sequence = 1, ExpectedDepartureAtUtc = now }] };
        seed.DeliveryRoutingPlans.Add(plan);
        await seed.SaveChangesAsync();
        var proposalId = plan.Proposals.Single().Id;
        var settings = Options.Create(new DeliveryRoutingOptions { MaximumFinalistsToValidate = 0 });
        var planner = new DeliveryRoutingPlanService(seed, clock, new ApproximateRoutingCostMatrixProvider(settings),
            new OrToolsDeliveryRouteOptimizer(settings), new KitchenPreparationEstimator(seed, settings),
            new DeliverymanAvailabilityService(seed, Mock.Of<IGoogleRoutesDrivingMetricsService>(), settings),
            Mock.Of<IGoogleRoutesDrivingMetricsService>(),
            Mock.Of<IOrderNotificationService>(), settings, NullLogger<DeliveryRoutingPlanService>.Instance);
        var preview = await planner.PreviewAsync(1, [3, 1], expectedPlanVersion: 1);
        Assert.Equal(new[] { 3, 1 }, preview.Stops.Select(s => s.OrderId));
        Assert.NotNull(preview.SelectionFingerprint);
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<bool> Claim(int courier)
        {
            await using var db = CreateDb();
            var repository = new OrderRepository(db, clock, TestTenantContext.Default);
            var proxy = new Mock<IOrderRepository>();
            proxy.Setup(r => r.AssignDeliveryManAsync(It.IsAny<int>(), courier, It.IsAny<CancellationToken>()))
                .Returns<int, int, CancellationToken>(async (id, driver, ct) =>
                {
                    if (id == 3)
                    {
                        if (Interlocked.Increment(ref arrivals) == 2) bothReady.TrySetResult();
                        await bothReady.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                    }
                    return await repository.AssignDeliveryManAsync(id, driver, ct);
                });
            proxy.Setup(r => r.ChangeStatusAsync(It.IsAny<int>(), OrderStatus.OnTheWay, null, It.IsAny<CancellationToken>()))
                .Returns<int, OrderStatus, string?, CancellationToken>(repository.ChangeStatusAsync);
            proxy.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<int, CancellationToken>(repository.GetByIdAsync);
            proxy.Setup(r => r.GetByIdWithFullDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<int, CancellationToken>(repository.GetByIdWithFullDetailsAsync);
            var user = new Mock<ICurrentUser>();
            user.SetupGet(u => u.Id).Returns(courier);
            user.SetupGet(u => u.BranchId).Returns(1);
            user.SetupGet(u => u.Role).Returns("Deliveryman");
            user.SetupGet(u => u.IsAuthenticated).Returns(true);
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<OrderDto>(It.IsAny<Order>())).Returns((Order order) => new OrderDto { Id = order.Id });
            var workflow = new DeliveryRouteWorkflowService(db, Mock.Of<IGoogleRoutesDrivingMetricsService>(),
                Options.Create(new DeliveryRouteOptions()), NullLogger<DeliveryRouteWorkflowService>.Instance, clock);
            var handler = new SelfAssignOrdersHandler(proxy.Object, mapper.Object, user.Object, Mock.Of<IOrderNotificationService>(),
                workflow, Mock.Of<IPrintQueueService>(), NullLogger<SelfAssignOrdersHandler>.Instance, db, clock: clock);
            try
            {
                await handler.Handle(new SelfAssignOrdersCommand { OrderIds = [3, 1], ProposalId = proposalId,
                    ExpectedPlanVersion = 1, ModifiedProposal = true, SelectionFingerprint = preview.SelectionFingerprint }, default);
                return true;
            }
            catch (RoutingPlanStaleException) { return false; }
        }
        var outcomes = await Task.WhenAll(Claim(24), Claim(25));
        Assert.Single(outcomes, success => success);
        seed.ChangeTracker.Clear();
        var route = Assert.Single(await seed.DeliveryRoutes.Include(r => r.Stops).ToListAsync());
        Assert.Equal(new[] { 3, 1 }, route.Stops.OrderBy(s => s.StopSequence).Select(s => s.OrderId));
        Assert.Equal(OrderStatus.Ready, (await seed.Orders.SingleAsync(o => o.Id == 2)).Status);
        Assert.Equal(2, await seed.Orders.CountAsync(o => o.DeliveryManId == route.DeliverymanId));
        await planner.RecalculateAsync(1);
        Assert.Equal(new[] { 3, 1 }, (await seed.DeliveryRouteStops.AsNoTracking()
            .Where(s => s.DeliveryRouteId == route.Id).OrderBy(s => s.StopSequence).ToListAsync()).Select(s => s.OrderId));
        await Assert.ThrowsAsync<RoutingPlanStaleException>(() => planner.PreviewAsync(1, [2], expectedPlanVersion: 1));
    }
}
