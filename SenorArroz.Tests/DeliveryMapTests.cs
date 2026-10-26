using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class DeliveryMapTests
{
    private sealed class LocalPostgreSqlFactAttribute : FactAttribute
    {
        public LocalPostgreSqlFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PERF_POSTGRES_CONNECTION")))
                Skip = "Requires an isolated PostgreSQL database in PERF_POSTGRES_CONNECTION.";
        }
    }

    [LocalPostgreSqlFact]
    public async Task PostgreSql_executes_projected_map_and_grouped_location_queries()
    {
        var tenant = new TestTenantContext(1);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("PERF_POSTGRES_CONNECTION")).Options,
            currentTenant: tenant, tenantExecutionContext: tenant);
        await db.Database.EnsureCreatedAsync();
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync())
        {
            using (tenant.BeginSystemScope())
            {
                db.Tenants.Add(new Tenant { Id = 1, Name = "Synthetic performance", Slug = "perf-map" });
                db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "Synthetic", Address = "Test", Phone1 = "1" });
                db.Users.Add(new User { Id = 10, TenantId = 1, BranchId = 1, Role = UserRole.Deliveryman, Email = "map@example.invalid" });
                await db.SaveChangesAsync();
                var date = new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc);
                db.Orders.Add(new Order { TenantId = 1, BranchId = 1, TakenById = 10, DeliveryManId = 10,
                    Type = OrderType.Delivery, Status = OrderStatus.OnTheWay, CreatedAt = date });
                db.DeliverymanLocations.AddRange(
                    new DeliverymanLocation { TenantId = 1, DeliverymanId = 10, Latitude = 1, RecordedAt = date.AddMinutes(-1) },
                    new DeliverymanLocation { TenantId = 1, DeliverymanId = 10, Latitude = 2, RecordedAt = date });
                await db.SaveChangesAsync();
            }
        }
        var result = await Handler(db).Handle(new(1, new DateTime(2026, 10, 26)), default);
        Assert.Equal("on_the_way", Assert.Single(result.Orders.Items).Status);
        Assert.Equal(2, Assert.Single(result.Locations).Latitude);
    }

    private static ApplicationDbContext Database()
    {
        var tenant = new TestTenantContext(1);
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: tenant, tenantExecutionContext: tenant);
    }
    private static GetDeliveryMapHandler Handler(ApplicationDbContext db)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Role).Returns("Cashier");
        user.SetupGet(x => x.TenantId).Returns(1);
        user.SetupGet(x => x.BranchId).Returns(1);
        var branches = new Mock<IBranchContext>();
        branches.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns((int? id) => id ?? 1);
        return new(db, user.Object, branches.Object);
    }

    [Fact]
    public async Task Pages_all_map_orders_and_groups_latest_locations_without_foreign_branches()
    {
        await using var db = Database();
        var date = new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc);
        db.Users.AddRange(new User { Id = 10, TenantId = 1, BranchId = 1, Role = UserRole.Deliveryman },
            new User { Id = 11, TenantId = 1, BranchId = 2, Role = UserRole.Deliveryman });
        for (var index = 0; index < 205; index++) db.Orders.Add(new Order { TenantId = 1, BranchId = 1,
            DeliveryManId = 10, Type = OrderType.Delivery, Status = OrderStatus.OnTheWay, CreatedAt = date });
        db.Orders.Add(new Order { TenantId = 1, BranchId = 2, DeliveryManId = 11,
            Type = OrderType.Delivery, Status = OrderStatus.OnTheWay, CreatedAt = date });
        db.DeliverymanLocations.AddRange(
            new DeliverymanLocation { TenantId = 1, DeliverymanId = 10, Latitude = 1, RecordedAt = date.AddMinutes(-1) },
            new DeliverymanLocation { TenantId = 1, DeliverymanId = 10, Latitude = 2, RecordedAt = date },
            new DeliverymanLocation { TenantId = 1, DeliverymanId = 11, Latitude = 3, RecordedAt = date });
        await db.SaveChangesAsync();
        var first = await Handler(db).Handle(new(1, date), default);
        Assert.Equal(205, first.Orders.TotalCount);
        Assert.Equal(200, first.Orders.Items.Count());
        Assert.Equal(2, Assert.Single(first.Locations).Latitude);
        var second = await Handler(db).Handle(new(1, date, 2), default);
        Assert.Equal(5, second.Orders.Items.Count());
        Assert.Empty(second.Locations);
        Assert.Empty(first.Orders.Items.Select(x => x.Id).Intersect(second.Orders.Items.Select(x => x.Id)));
    }

    [Fact]
    public async Task Rejects_cross_branch_and_propagates_cancellation()
    {
        await using var db = Database();
        await Assert.ThrowsAsync<BranchAccessDeniedException>(() => Handler(db).Handle(new(2, null), default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(db).Handle(new(1, null), cancellation.Token));
    }
}
