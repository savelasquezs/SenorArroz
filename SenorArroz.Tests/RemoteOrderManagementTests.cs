using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Behaviors;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Orders.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class RemoteOrderManagementTests
{
    private sealed record Actor(string Role = "Cashier", int BranchId = 1, int TenantId = 1) : ICurrentUser
    {
        public int Id => 70;
        public bool IsAuthenticated => true;
    }
    private sealed class Clock : IClock { public DateTime UtcNow => new(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc); }
    private sealed class Precondition(int id, long version) : IOrderMutationContext
    {
        public (int OrderId, long Version)? Expected => (id, version);
        public long? CompletedVersion { get; private set; }
        public void Complete(int orderId, long newVersion) => CompletedVersion = newVersion;
    }
    private static Order Remote(int id = 10, int? origin = 1) => new()
    {
        Id = id, TenantId = 1, OriginBranchId = origin, BranchId = 2,
        Type = OrderType.Delivery, Status = OrderStatus.Taken, TakenById = 70,
        CreatedAt = new Clock().UtcNow, UpdatedAt = new Clock().UtcNow,
        StatusTimes = "{}", ManagementVersion = 1
    };
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    private static async Task SeedBranches(ApplicationDbContext db)
    {
        db.Branches.AddRange(Enumerable.Range(1, 3).Select(id => new Branch
        { Id = id, TenantId = 1, Name = $"Sede {id}", Address = "Calle 1", Phone1 = "3000000000" }));
        db.Users.Add(new User { Id = 70, BranchId = 1, Name = "Caja", Email = "caja@test", PasswordHash = "x", Role = UserRole.Cashier });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Cashier")]
    [InlineData("Superadmin")]
    public void Source_management_roles_can_manage_only_the_originated_order(string role)
    {
        OrderAccessRules.EnsureAccess(Remote(), new Actor(role), new TestBranchContext(1));
        Assert.ThrowsAny<Exception>(() => OrderAccessRules.EnsureAccess(Remote(origin: 3), new Actor(role), new TestBranchContext(1)));
        Assert.ThrowsAny<Exception>(() => OrderAccessRules.EnsureAccess(Remote(origin: null), new Actor(role), new TestBranchContext(1)));
    }

    [Theory]
    [InlineData("Kitchen")]
    [InlineData("Deliveryman")]
    public void Operational_roles_do_not_get_remote_privileges(string role) =>
        Assert.ThrowsAny<Exception>(() => OrderAccessRules.EnsureAccess(Remote(), new Actor(role), new TestBranchContext(1)));

    [Fact]
    public void Tenant_boundary_wins_even_for_superadmin()
    {
        var order = Remote(); order.TenantId = 2;
        Assert.Throws<BranchAccessDeniedException>(() => OrderAccessRules.EnsureAccess(order, new Actor("Superadmin"), new TestBranchContext(1)));
    }

    [Fact]
    public void Provenance_does_not_follow_an_employee_who_moves_branch()
    {
        Assert.ThrowsAny<Exception>(() => OrderAccessRules.EnsureAccess(Remote(), new Actor(BranchId: 3), new TestBranchContext(3)));
        OrderAccessRules.EnsureAccess(Remote(), new Actor(BranchId: 2), new TestBranchContext(2));
        Assert.Throws<BusinessException>(() => OrderAccessRules.EnsurePaymentBranch(Remote(), 1));
    }

    [Fact]
    public async Task Remote_search_filters_origin_before_count_and_pagination()
    {
        await using var db = Db(); await SeedBranches(db);
        db.Orders.AddRange(Remote(10), Remote(11), Remote(12, 3));
        await db.SaveChangesAsync();
        var repo = new OrderRepository(db, new Clock());
        var page = await repo.SearchOrdersAsync(branchId: 2, originBranchId: 1, page: 1, pageSize: 1);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.All(page.Items, o => Assert.Equal(1, o.OriginBranchId));
        var local = await repo.SearchOrdersAsync(branchId: 2, page: 1, pageSize: 10);
        Assert.Equal(3, local.TotalCount);
        var service = new OrderManagementService(db, new Actor(), new TestBranchContext(1), new Clock());
        var tabs = await service.GetRemoteBranches(default);
        Assert.Single(tabs); Assert.Equal(2, tabs[0].Id);
    }

    [Fact]
    public async Task Mutation_rejects_stale_version_before_calling_the_handler()
    {
        await using var db = Db(); db.Orders.Add(Remote()); await db.SaveChangesAsync();
        var behavior = new OrderMutationBehavior<UpdateOrderCommand, OrderDto>(db, new Actor(), new TestBranchContext(1),
            Mock.Of<IOrderNotificationService>(), NullLogger<OrderMutationBehavior<UpdateOrderCommand, OrderDto>>.Instance,
            new Precondition(10, 999));
        var invoked = false;
        await Assert.ThrowsAsync<OrderConcurrencyException>(() => behavior.Handle(new UpdateOrderCommand { Id = 10 }, _ =>
        { invoked = true; return Task.FromResult(new OrderDto()); }, default));
        Assert.False(invoked);
        Assert.Equal(1, (await db.Orders.SingleAsync()).ManagementVersion);
    }

    [Fact]
    public async Task Remote_mutation_requires_a_snapshot_even_when_client_omits_the_header()
    {
        await using var db = Db(); db.Orders.Add(Remote()); await db.SaveChangesAsync();
        var behavior = new OrderMutationBehavior<UpdateOrderCommand, OrderDto>(db, new Actor(), new TestBranchContext(1),
            Mock.Of<IOrderNotificationService>(), NullLogger<OrderMutationBehavior<UpdateOrderCommand, OrderDto>>.Instance);
        await Assert.ThrowsAsync<OrderConcurrencyException>(() => behavior.Handle(new UpdateOrderCommand { Id = 10 },
            _ => Task.FromResult(new OrderDto()), default));
    }

    [Fact]
    public async Task Successful_mutation_increments_version_and_notifies_origin_and_destination_metadata()
    {
        await using var db = Db(); db.Orders.Add(Remote()); await db.SaveChangesAsync();
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(n => n.NotifyOrderManagementChanged(10, 2, 1, It.IsAny<long>())).Returns(Task.CompletedTask);
        var expected = new Precondition(10, 1);
        var behavior = new OrderMutationBehavior<UpdateOrderCommand, OrderDto>(db, new Actor(), new TestBranchContext(1),
            notifications.Object, NullLogger<OrderMutationBehavior<UpdateOrderCommand, OrderDto>>.Instance, expected);
        var result = await behavior.Handle(new UpdateOrderCommand { Id = 10 }, _ => Task.FromResult(new OrderDto { Id = 10 }), default);
        Assert.Equal(2, result.ManagementVersion); Assert.Equal(2, expected.CompletedVersion);
        var audit = Assert.Single(await db.EntityAuditLogs.ToListAsync());
        Assert.Equal(70, audit.ChangedByUserId); Assert.Equal(2, audit.BranchId);
        Assert.Contains("workspaceBranchId", audit.MetadataJson);
        notifications.Verify(n => n.NotifyOrderManagementChanged(10, 2, 1, 2), Times.Once);
    }

    [Fact]
    public async Task Origin_is_immutable_in_application_writes()
    {
        await using var db = Db(); var order = Remote(); db.Orders.Add(order); await db.SaveChangesAsync();
        order.OriginBranchId = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Tracking_does_not_reveal_a_courier_for_an_unrelated_remote_order()
    {
        await using var db = Db(); db.Orders.Add(Remote(origin: 3)); await db.SaveChangesAsync();
        var service = new OrderManagementService(db, new Actor(), new TestBranchContext(1), new Clock());
        await Assert.ThrowsAnyAsync<Exception>(() => service.GetTracking(10, default));
    }

    [Fact]
    public async Task Tracking_stops_after_delivery_and_options_use_destination()
    {
        await using var db = Db(); await SeedBranches(db);
        var order = Remote(); order.Status = OrderStatus.Delivered; db.Orders.Add(order);
        db.Banks.AddRange(new Bank { Id = 1, BranchId = 1, Name = "Origen" }, new Bank { Id = 2, BranchId = 2, Name = "Destino" });
        db.Users.AddRange(new User { Id = 1, BranchId = 1, Name = "Local", Email = "a@test", PasswordHash = "x", Role = UserRole.Deliveryman },
            new User { Id = 2, BranchId = 2, Name = "Remoto", Email = "b@test", PasswordHash = "x", Role = UserRole.Deliveryman });
        await db.SaveChangesAsync();
        var service = new OrderManagementService(db, new Actor(), new TestBranchContext(1), new Clock());
        Assert.Null((await service.GetTracking(10, default)).Location);
        var options = await service.GetOptions(10, default);
        Assert.Equal(2, Assert.Single(options.Banks).BranchId);
        Assert.Equal("Remoto", Assert.Single(options.Deliverymen).Name);
    }

    [Theory]
    [InlineData("fresh", true, true)]
    [InlineData("stale", true, false)]
    [InlineData("different-route", false, false)]
    [InlineData("closed-session", false, false)]
    [InlineData("wrong-branch", false, false)]
    [InlineData("completed-route", false, false)]
    [InlineData("cancelled-order", false, false)]
    public async Task Tracking_exposes_only_the_current_delivery_session(string scenario, bool visible, bool fresh)
    {
        await using var db = Db(); await SeedBranches(db);
        var now = new Clock().UtcNow;
        var driver = new User { Id = 80, BranchId = 2, Name = "Driver", Email = "driver@test", PasswordHash = "x", Role = UserRole.Deliveryman };
        db.Users.Add(driver);
        var route = new DeliveryRoute { Id = 40, BranchId = 2, DeliverymanId = 80, Status = DeliveryRouteStatus.InProgress };
        if (scenario == "completed-route") route.Status = DeliveryRouteStatus.Completed;
        db.DeliveryRoutes.Add(route);
        var session = new DeliveryWorkSession { Id = 50, BranchId = scenario == "wrong-branch" ? 1 : 2,
            DeliverymanId = 80, StartedAt = now.AddHours(-1), AutoCloseAt = now.AddHours(4),
            Status = scenario == "closed-session" ? DeliveryWorkSessionStatus.Closed : DeliveryWorkSessionStatus.Active };
        db.DeliveryWorkSessions.Add(session);
        var order = Remote(); order.DeliveryManId = 80; order.DeliveryRouteId = 40;
        order.Status = scenario == "cancelled-order" ? OrderStatus.Cancelled : OrderStatus.OnTheWay;
        db.Orders.Add(order);
        db.DeliverymanLocations.Add(new DeliverymanLocation { Id = 1, DeliverymanId = 80, WorkSessionId = 50,
            DeliveryRouteId = 40, Latitude = 6.2m, Longitude = -75.5m,
            RecordedAt = scenario == "stale" ? now.AddMinutes(-5) : now.AddSeconds(-20) });
        if (scenario == "different-route")
            db.DeliverymanLocations.Add(new DeliverymanLocation { Id = 2, DeliverymanId = 80, WorkSessionId = 50,
                DeliveryRouteId = 41, Latitude = 6.3m, Longitude = -75.6m, RecordedAt = now.AddSeconds(-5) });
        await db.SaveChangesAsync();
        var result = await new OrderManagementService(db, new Actor(), new TestBranchContext(1), new Clock()).GetTracking(10, default);
        Assert.Equal(visible, result.Location is not null);
        if (result.Location is not null) Assert.Equal(fresh, result.Location.IsFresh);
    }
}
