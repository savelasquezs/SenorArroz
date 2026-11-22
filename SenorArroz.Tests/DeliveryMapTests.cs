using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
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
    public async Task Incomplete_first_map_page_preserves_totals_and_empty_day()
    {
        await using var db = Database();
        var date = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User { Id = 10, TenantId = 1, BranchId = 1, Role = UserRole.Deliveryman });
        db.Orders.AddRange(Enumerable.Range(0, 3).Select(_ => new Order
        {
            TenantId = 1, BranchId = 1, DeliveryManId = 10,
            Type = OrderType.Delivery, Status = OrderStatus.OnTheWay, CreatedAt = date
        }));
        await db.SaveChangesAsync();

        var shortPage = await Handler(db).Handle(new(1, new DateTime(2026, 11, 20), PageSize: 10), default);
        Assert.Equal(3, shortPage.Orders.TotalCount);
        Assert.Equal(1, shortPage.Orders.TotalPages);
        Assert.Equal(3, shortPage.Orders.Items.Count());

        var fullPage = await Handler(db).Handle(new(1, new DateTime(2026, 11, 20), PageSize: 3), default);
        Assert.Equal(3, fullPage.Orders.TotalCount);
        Assert.Equal(1, fullPage.Orders.TotalPages);
        Assert.Equal(3, fullPage.Orders.Items.Count());

        var noOrders = await Handler(db).Handle(new(1, new DateTime(2026, 11, 19), PageSize: 10), default);
        Assert.Equal(0, noOrders.Orders.TotalCount);
        Assert.Equal(0, noOrders.Orders.TotalPages);
        Assert.Empty(noOrders.Orders.Items);
    }

    private sealed class MapCountInterceptor : DbCommandInterceptor
    {
        private int _countQueries;
        public int CountQueries => Volatile.Read(ref _countQueries);
        public void Reset() => Interlocked.Exchange(ref _countQueries, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Regex.IsMatch(command.CommandText, @"^\s*SELECT\s+count\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                Interlocked.Increment(ref _countQueries);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PostgreSql_count_is_skipped_only_for_incomplete_first_map_page()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        var tenant = new TestTenantContext(1);
        var interceptor = new MapCountInterceptor();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(interceptor).Options,
            currentTenant: tenant, tenantExecutionContext: tenant);
        await db.Database.EnsureCreatedAsync();
        using (tenant.BeginSystemScope())
        {
            db.Tenants.Add(new Tenant { Id = 1, Name = "Map count test", Slug = "map-count-test" });
            await db.SaveChangesAsync();
            db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "Test", Address = "Test", Phone1 = "1" });
            await db.SaveChangesAsync();
            db.Users.Add(new User
            {
                Id = 10, TenantId = 1, BranchId = 1, Role = UserRole.Deliveryman,
                Name = "Synthetic driver", Email = "map-count@example.invalid", PasswordHash = "test-only", Active = true
            });
            await db.SaveChangesAsync();
            var timestamp = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
            db.Orders.AddRange(Enumerable.Range(0, 3).Select(_ => new Order
            {
                TenantId = 1, BranchId = 1, TakenById = 10, DeliveryManId = 10,
                Type = OrderType.Delivery, Status = OrderStatus.OnTheWay, CreatedAt = timestamp,
                StatusTimes = "{}", Total = 10000, Subtotal = 10000
            }));
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();

        async Task Check(DateTime day, int page, int size, int total, int rows, int expectedCounts)
        {
            interceptor.Reset();
            var result = await Handler(db).Handle(new(1, day, page, size), default);
            Assert.Equal(total, result.Orders.TotalCount);
            Assert.Equal(rows, result.Orders.Items.Count());
            Assert.Equal(expectedCounts, interceptor.CountQueries);
        }

        var day = new DateTime(2026, 11, 20);
        await Check(day, 1, 10, 3, 3, 0); // One paged SELECT, no COUNT
        await Check(day.AddDays(-1), 1, 10, 0, 0, 0);
        await Check(day, 1, 3, 3, 3, 1);  // Exact full first page still needs COUNT
        await Check(day, 2, 2, 3, 1, 1);  // Later page still needs COUNT
        await Check(day, 3, 2, 3, 0, 1);  // Out-of-range page still needs COUNT
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
