using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica rangos y agregación de pedidos por cliente, estados excluidos y ausencia de pedidos.
/// </summary>
public class OrderDateRangeTests
{
    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    }

    private static async Task<(Branch branch, User user, Customer customer)> SeedBaseEntitiesAsync(
        ApplicationDbContext ctx)
    {
        var branch = new Branch { Name = "Test", Address = "-", Phone1 = "-", CreatedAt = DateTime.UtcNow };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            Name = "Cajero",
            Email = $"cajero_{Guid.NewGuid()}@test.com",
            PasswordHash = "hash",
            Role = UserRole.Cashier,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var customer = new Customer
        {
            Name = "Cliente Test",
            Phone1 = "3001234567",
            BranchId = branch.Id,
            Active = true,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        return (branch, user, customer);
    }

    private static Order MakeOrder(int branchId, int takenById, int customerId, OrderStatus status, DateTime createdAt)
        => new()
        {
            BranchId = branchId,
            TakenById = takenById,
            CustomerId = customerId,
            Status = status,
            Type = OrderType.Onsite,
            Total = 10000,
            Subtotal = 10000,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Excluye órdenes canceladas: solo las no-canceladas contribuyen al rango
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderDateRange_ExcludesCancelledOrders()
    {
        using var ctx = CreateCtx(nameof(GetOrderDateRange_ExcludesCancelledOrders));
        var (branch, user, customer) = await SeedBaseEntitiesAsync(ctx);

        var validDate1 = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var validDate2 = new DateTime(2024, 3, 20, 0, 0, 0, DateTimeKind.Utc);
        var cancelledDate = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc); // más reciente pero cancelada

        ctx.Orders.AddRange(
            MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, validDate1),
            MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, validDate2),
            MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Cancelled,  cancelledDate)
        );
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var stats = (await repo.GetOrderStatisticsForCustomersAsync(new[] { customer.Id }))[customer.Id];
        var (first, last) = (stats.FirstOrderDate, stats.LastOrderDate);

        Assert.Equal(validDate1, first);
        Assert.Equal(validDate2, last);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Con una sola orden no-cancelada: First == Last
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderDateRange_SingleOrder_FirstEqualsLast()
    {
        using var ctx = CreateCtx(nameof(GetOrderDateRange_SingleOrder_FirstEqualsLast));
        var (branch, user, customer) = await SeedBaseEntitiesAsync(ctx);

        var orderDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        ctx.Orders.Add(MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, orderDate));
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var stats = (await repo.GetOrderStatisticsForCustomersAsync(new[] { customer.Id }))[customer.Id];
        var (first, last) = (stats.FirstOrderDate, stats.LastOrderDate);

        Assert.NotNull(first);
        Assert.NotNull(last);
        Assert.Equal(first, last);
        Assert.Equal(orderDate, first);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Cliente sin órdenes → (null, null)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderDateRange_NoOrders_ReturnsNullTuple()
    {
        using var ctx = CreateCtx(nameof(GetOrderDateRange_NoOrders_ReturnsNullTuple));
        var (_, _, customer) = await SeedBaseEntitiesAsync(ctx);

        var repo = new CustomerRepository(ctx);
        var stats = await repo.GetOrderStatisticsForCustomersAsync(new[] { customer.Id });

        Assert.Empty(stats);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Múltiples órdenes: First es la más antigua, Last la más reciente
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrderDateRange_MultipleOrders_CorrectMinMax()
    {
        using var ctx = CreateCtx(nameof(GetOrderDateRange_MultipleOrders_CorrectMinMax));
        var (branch, user, customer) = await SeedBaseEntitiesAsync(ctx);

        var oldest = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var middle = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var newest = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        ctx.Orders.AddRange(
            MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, middle),
            MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, oldest),
            MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, newest)
        );
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var stats = (await repo.GetOrderStatisticsForCustomersAsync(new[] { customer.Id }))[customer.Id];
        var (first, last) = (stats.FirstOrderDate, stats.LastOrderDate);

        Assert.Equal(oldest, first);
        Assert.Equal(newest, last);
    }
    [Fact]
    public async Task GetOrderStatistics_ExcludesAwaitingPaymentAndCancelled_AndSeparatesDelivered()
    {
        using var ctx = CreateCtx(nameof(GetOrderStatistics_ExcludesAwaitingPaymentAndCancelled_AndSeparatesDelivered));
        var (branch, user, customer) = await SeedBaseEntitiesAsync(ctx);
        var day = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

        var delivered = MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Delivered, day);
        delivered.Total = 12000;
        var taken = MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Taken, day.AddMinutes(10));
        taken.Total = 8000;
        var awaiting = MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.AwaitingPayment, day.AddMinutes(20));
        awaiting.Total = 9000;
        var cancelled = MakeOrder(branch.Id, user.Id, customer.Id, OrderStatus.Cancelled, day.AddMinutes(30));
        cancelled.Total = 7000;
        ctx.Orders.AddRange(delivered, taken, awaiting, cancelled);
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var stats = (await repo.GetOrderStatisticsForCustomersAsync(new[] { customer.Id, customer.Id }))[customer.Id];

        Assert.Equal(2, stats.TotalOrders);
        Assert.Equal(1, stats.DeliveredOrders);
        Assert.Equal(20000, stats.TotalAccumulated);
        Assert.Equal(day, stats.FirstOrderDate);
        Assert.Equal(day.AddMinutes(10), stats.LastOrderDate);
    }

}
