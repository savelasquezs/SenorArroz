using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica que GetOrderDateRangeAsync devuelve las fechas correctas en una sola query,
/// excluyendo órdenes canceladas y manejando el caso de cliente sin órdenes.
/// </summary>
public class OrderDateRangeTests
{
    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts);
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
        var (first, last) = await repo.GetOrderDateRangeAsync(customer.Id);

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
        var (first, last) = await repo.GetOrderDateRangeAsync(customer.Id);

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
        var (first, last) = await repo.GetOrderDateRangeAsync(customer.Id);

        Assert.Null(first);
        Assert.Null(last);
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
        var (first, last) = await repo.GetOrderDateRangeAsync(customer.Id);

        Assert.Equal(oldest, first);
        Assert.Equal(newest, last);
    }
}
