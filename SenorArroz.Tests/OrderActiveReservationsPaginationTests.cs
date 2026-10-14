using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>Active reservations must be filtered in SQL before pagination and COUNT.</summary>
public class OrderActiveReservationsPaginationTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);

    private static async Task<int> SeedAsync(ApplicationDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Name = "Reservation filter test", Slug = "reservation-filter-test" });
        await db.SaveChangesAsync();
        var branch = new Branch { Name = "Santander test", Address = "Test", Phone1 = "0", CreatedAt = Now };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var cashier = new User
        {
            Name = "Cashier", Email = $"reservation_{Guid.NewGuid():N}@example.com",
            PasswordHash = "test-only", Role = UserRole.Cashier,
            BranchId = branch.Id, Active = true, CreatedAt = Now
        };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();

        var statuses = new[]
        {
            OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Taken,
            OrderStatus.InPreparation, OrderStatus.Ready
        };
        for (var i = 0; i < statuses.Length; i++)
        {
            db.Orders.Add(new Order
            {
                BranchId = branch.Id, TakenById = cashier.Id,
                Type = OrderType.Reservation, Status = statuses[i],
                GuestName = i == 2 ? "Reserva distintiva" : $"Reserva {i}",
                ReservedFor = Now.AddDays(1), StatusTimes = "{}",
                Subtotal = 10000, Total = 10000,
                CreatedAt = Now.AddMinutes(i), UpdatedAt = Now.AddMinutes(i)
            });
        }
        db.Orders.Add(new Order
        {
            BranchId = branch.Id, TakenById = cashier.Id,
            Type = OrderType.Delivery, Status = OrderStatus.Taken,
            StatusTimes = "{}", Subtotal = 10000, Total = 10000,
            CreatedAt = Now, UpdatedAt = Now
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return branch.Id;
    }

    private static async Task VerifyAsync(ApplicationDbContext db)
    {
        var branchId = await SeedAsync(db);
        var repository = new OrderRepository(db, new FakeClock(Now));

        var first = await repository.SearchOrdersAsync(
            branchId: branchId, type: OrderType.Reservation,
            activeReservationsOnly: true, page: 1, pageSize: 2, sortBy: "createdAt");
        var second = await repository.SearchOrdersAsync(
            branchId: branchId, type: OrderType.Reservation,
            activeReservationsOnly: true, page: 2, pageSize: 2, sortBy: "createdAt");

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, first.Items.Count());
        Assert.Single(second.Items);
        var all = first.Items.Concat(second.Items).ToList();
        Assert.Equal(3, all.Select(o => o.Id).Distinct().Count());
        Assert.All(all, o =>
        {
            Assert.Equal(OrderType.Reservation, o.Type);
            Assert.NotEqual(OrderStatus.Delivered, o.Status);
            Assert.NotEqual(OrderStatus.Cancelled, o.Status);
        });

        var allStatuses = await repository.SearchOrdersAsync(
            branchId: branchId, type: OrderType.Reservation,
            page: 1, pageSize: 10);
        Assert.Equal(5, allStatuses.TotalCount);

        var delivered = await repository.SearchOrdersAsync(
            branchId: branchId, type: OrderType.Reservation, status: OrderStatus.Delivered,
            page: 1, pageSize: 10);
        Assert.Single(delivered.Items);
    }

    [Fact]
    public async Task Active_filter_before_pagination_in_memory()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        await VerifyAsync(db);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Active_filter_before_pagination_on_postgresql()
    {
        await using var pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await pg.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(pg.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();
        await VerifyAsync(db);
    }
}
