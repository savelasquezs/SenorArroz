using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.PostgreSql;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class RoutingPlanPublicationPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task FinalistValidationDoesNotHoldTransactionAndStopsKeepBothForeignKeys()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = 1, Name = "Test", Slug = "routing-test" });
        db.Branches.Add(new Branch { Id = 1, Name = "Branch", Address = "Store", Phone1 = "3000000000", Latitude = 6.2m, Longitude = -75.5m });
        db.Users.Add(new User { Id = 1, BranchId = 1, Role = UserRole.Admin, Name = "Test", Email = "routing@test.local", Phone = "3000000001", PasswordHash = "test" });
        db.Customers.Add(new Customer { Id = 1, BranchId = 1, Name = "Test", Phone1 = "3000000002" });
        db.Neighborhoods.Add(new Neighborhood { Id = 1, BranchId = 1, Name = "Test" });
        db.Addresses.Add(new Address { Id = 1, CustomerId = 1, NeighborhoodId = 1, AddressText = "Test address", Latitude = 6.201m, Longitude = -75.501m });
        db.Orders.Add(new Order { Id = 1, BranchId = 1, TakenById = 1, CustomerId = 1, AddressId = 1, Type = OrderType.Delivery, Status = OrderStatus.Ready });
        await db.SaveChangesAsync();
        var options = Options.Create(new DeliveryRoutingOptions { MaximumFinalistsToValidate = 1 });
        var estimator = new Mock<IKitchenPreparationEstimator>();
        estimator.Setup(e => e.EstimateAsync(1, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, KitchenPreparationEstimate> { [1] = new(now, "high") });
        var availability = new Mock<IDeliverymanAvailabilityService>();
        availability.Setup(a => a.GetCapacityAsync(1, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryRoutingCapacity([new RoutingVehicleSlot(0)], 1, 0, []));
        var optimizer = new Mock<IDeliveryRouteOptimizer>();
        optimizer.Setup(o => o.Optimize(It.IsAny<DeliveryRouteOptimizationRequest>()))
            .Returns(new DeliveryRouteOptimizationResult([new OptimizedRoute(0, [0], 100, 100, 1)], [], 1, []));
        var google = new Mock<IGoogleRoutesDrivingMetricsService>();
        google.Setup(g => g.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Assert.Null(db.Database.CurrentTransaction);
                return Task.FromResult(new DrivingRouteMetrics(200, 120, 100, 60));
            });
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(n => n.NotifyDeliveryRoutingPlanChanged(1, It.IsAny<int>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);
        var service = new DeliveryRoutingPlanService(db, new FakeClock(now), new ApproximateRoutingCostMatrixProvider(options),
            optimizer.Object, estimator.Object, availability.Object, google.Object, notifications.Object,
            options, Mock.Of<ILogger<DeliveryRoutingPlanService>>());
        var plan = await service.GetOrCreateActivePlanAsync(1);
        var proposal = Assert.Single(plan.Proposals);
        var stop = Assert.Single(await db.DeliveryRouteProposalStops.AsNoTracking().ToListAsync());
        Assert.Equal(plan.Id, stop.DeliveryRoutingPlanId);
        Assert.Equal(proposal.Id, stop.DeliveryRouteProposalId);
        Assert.Equal(new[] { 1 }, proposal.ClaimableReadyOrderIds);
        Assert.Equal("validated", proposal.GoogleValidationStatus);
        Assert.Equal(plan.Id, (await service.GetOrCreateActivePlanAsync(1)).Id);
        google.Verify(g => g.ComputeRouteAsync(It.IsAny<IReadOnlyList<(double Latitude, double Longitude)>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
