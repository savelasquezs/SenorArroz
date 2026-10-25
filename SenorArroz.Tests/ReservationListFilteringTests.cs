using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>M12-F01: reservas activas deben filtrarse antes de paginar y contar.</summary>
public sealed class ReservationListFilteringTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc);

    private static async Task<int> SeedAsync(ApplicationDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Name = "Reservation test", Slug = "reservation-list-test" });
        await db.SaveChangesAsync();
        var branch = new Branch { Name = "Branch", Address = "Synthetic", Phone1 = "0", CreatedAt = Now };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var cashier = new User
        {
            Name = "Cashier", Email = "reservation-test@example.invalid", PasswordHash = "test-only",
            BranchId = branch.Id, Active = true, Role = UserRole.Cashier, CreatedAt = Now
        };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();

        // Closed reservations are most recent, and otherwise displace active rows on page 1.
        var rows = new (OrderType type, OrderStatus status, string notes)[]
        {
            (OrderType.Reservation, OrderStatus.Delivered, "MATCH only closed"),
            (OrderType.Reservation, OrderStatus.Cancelled, "other closed"),
            (OrderType.Reservation, OrderStatus.Taken, "MATCH open"),
            (OrderType.Reservation, OrderStatus.Ready, "other open"),
            (OrderType.Reservation, OrderStatus.OnTheWay, "other open"),
            (OrderType.Onsite, OrderStatus.Taken, "MATCH non-reservation")
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var (type, status, notes) = rows[i];
            db.Orders.Add(new Order
            {
                BranchId = branch.Id, TakenById = cashier.Id, Type = type, Status = status,
                Notes = notes, ReservedFor = type == OrderType.Reservation ? Now.AddDays(1) : null,
                Subtotal = 10000, Total = 10000, StatusTimes = "{}",
                CreatedAt = Now.AddMinutes(-i), UpdatedAt = Now.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return branch.Id;
    }

    private static OrderRepository Repository(ApplicationDbContext db) =>
        new(db, new FakeClock(Now));

    private static async Task AssertPageContractAsync(ApplicationDbContext db, int branch)
    {
        var repository = Repository(db);
        var first = await repository.SearchOrdersAsync(branchId: branch,
            activeReservationsOnly: true, page: 1, pageSize: 2, sortBy: "createdAt", sortOrder: "desc");
        var second = await repository.SearchOrdersAsync(branchId: branch,
            activeReservationsOnly: true, page: 2, pageSize: 2, sortBy: "createdAt", sortOrder: "desc");

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, first.Items.Count());
        Assert.Single(second.Items);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, second.TotalPages);
        Assert.All(first.Items.Concat(second.Items), row =>
        {
            Assert.Equal(OrderType.Reservation, row.Type);
            Assert.NotEqual(OrderStatus.Delivered, row.Status);
            Assert.NotEqual(OrderStatus.Cancelled, row.Status);
            Assert.Equal(branch, row.BranchId);
        });

        // Explicit "Todas" and "Entregada" keep their previous meanings.
        var all = await repository.SearchOrdersAsync(branchId: branch,
            type: OrderType.Reservation, pageSize: 20);
        Assert.Equal(5, all.TotalCount);
        var delivered = await repository.SearchOrdersAsync(branchId: branch,
            type: OrderType.Reservation, status: OrderStatus.Delivered, pageSize: 20);
        Assert.Single(delivered.Items);
        var cancelled = await repository.SearchOrdersAsync(branchId: branch,
            type: OrderType.Reservation, status: OrderStatus.Cancelled, pageSize: 20);
        Assert.Single(cancelled.Items);
    }

    [Fact]
    public async Task Active_reservations_are_filtered_before_count_and_pagination()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var branch = await SeedAsync(db);
        await AssertPageContractAsync(db, branch);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PostgreSql_active_reservation_search_preserves_server_pagination_and_text_search()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();
        var branch = await SeedAsync(db);
        await AssertPageContractAsync(db, branch);

        // Notes/IDs/customers are searched by the pre-existing parameterized ILIKE filter.
        var match = await Repository(db).SearchOrdersAsync(branchId: branch,
            type: OrderType.Reservation, activeReservationsOnly: true,
            searchTerm: "MATCH", page: 1, pageSize: 2);
        Assert.Single(match.Items);
        Assert.Equal("MATCH open", match.Items.Single().Notes);
        Assert.Equal(1, match.TotalCount);
    }
}
