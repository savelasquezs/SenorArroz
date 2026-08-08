using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica que CancellationToken = default preserva la compatibilidad y que
/// un token ya cancelado es respetado por las llamadas EF Core.
/// </summary>
public class CancellationTokenRegressionTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Branch MakeBranch(string name = "Test Branch") => new()
    {
        Name = name,
        Address = "Calle 1",
        Phone1 = "0000",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CustomerRepository.GetPagedAsync con CT=default devuelve resultados
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CustomerRepository_GetPaged_WithDefaultToken_ReturnsResults()
    {
        const string db = nameof(CustomerRepository_GetPaged_WithDefaultToken_ReturnsResults);
        using var ctx = CreateContext(db);

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        ctx.Customers.AddRange(
            new Customer { Name = "Alice", Phone1 = "111", BranchId = branch.Id, Active = true, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Bob",   Phone1 = "222", BranchId = branch.Id, Active = true, CreatedAt = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var result = await repo.GetPagedAsync(
            branchId: branch.Id,
            page: 1,
            pageSize: 10,
            cancellationToken: default);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. BranchRepository.CreateAsync con token ya cancelado lanza excepción
    //    (SaveChangesAsync sí respeta CancellationToken en el proveedor InMemory)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task BranchRepository_Create_WithCancelledToken_ThrowsOperationCancelledException()
    {
        const string db = nameof(BranchRepository_Create_WithCancelledToken_ThrowsOperationCancelledException);
        using var ctx = CreateContext(db);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var repo = new BranchRepository(ctx, new SystemUtcClock());
        var newBranch = MakeBranch("Cancelled Branch");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repo.CreateAsync(newBranch, cts.Token));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. OrderRepository.SearchOrdersAsync con CT=default devuelve resultados
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task OrderRepository_SearchOrders_WithDefaultToken_ReturnsResults()
    {
        const string db = nameof(OrderRepository_SearchOrders_WithDefaultToken_ReturnsResults);
        using var ctx = CreateContext(db);

        var branch = MakeBranch("Order Branch");
        ctx.Branches.Add(branch);

        var user = new User { Name = "Cashier", Email = "cashier@test.com" };
        ctx.Users.Add(user);

        await ctx.SaveChangesAsync();

        ctx.Orders.AddRange(
            new Order
            {
                BranchId = branch.Id,
                TakenById = user.Id,
                Status = OrderStatus.Taken,
                Type = OrderType.Onsite,
                Total = 10000,
                Subtotal = 10000,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Order
            {
                BranchId = branch.Id,
                TakenById = user.Id,
                Status = OrderStatus.Delivered,
                Type = OrderType.Onsite,
                Total = 20000,
                Subtotal = 20000,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx, new SystemUtcClock());
        var result = await repo.SearchOrdersAsync(
            branchId: branch.Id,
            page: 1,
            pageSize: 10,
            cancellationToken: default);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task OrderRepository_TransferVerification_UsesPaymentCreatedAtInsteadOfOrderDate()
    {
        const string db = nameof(OrderRepository_TransferVerification_UsesPaymentCreatedAtInsteadOfOrderDate);
        using var ctx = CreateContext(db);

        var branch = MakeBranch("Transfer Branch");
        ctx.Branches.Add(branch);
        var user = new User { Name = "Cashier", Email = "transfer-cashier@test.com" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var bank = new Bank { BranchId = branch.Id, Name = "Banco de prueba" };
        ctx.Banks.Add(bank);
        var yesterday = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var paymentToday = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);
        var paymentYesterday = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var firstOrder = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Reservation,
            Total = 10000,
            Subtotal = 10000,
            CreatedAt = yesterday,
            UpdatedAt = yesterday,
        };
        var secondOrder = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Onsite,
            Total = 20000,
            Subtotal = 20000,
            CreatedAt = yesterday,
            UpdatedAt = yesterday,
        };
        var highestOrder = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Onsite,
            Total = 30000,
            Subtotal = 30000,
            CreatedAt = yesterday,
            UpdatedAt = yesterday,
        };
        ctx.Orders.AddRange(firstOrder, secondOrder, highestOrder);
        await ctx.SaveChangesAsync();
        ctx.BankPayments.AddRange(
            new BankPayment
            {
                OrderId = firstOrder.Id,
                BankId = bank.Id,
                Amount = 10000,
                IsVerified = false,
                CreatedAt = paymentToday,
                UpdatedAt = paymentToday,
            },
            new BankPayment
            {
                OrderId = secondOrder.Id,
                BankId = bank.Id,
                Amount = 20000,
                IsVerified = false,
                CreatedAt = paymentYesterday,
                UpdatedAt = paymentYesterday,
            });
        ctx.BankPayments.Add(new BankPayment
        {
            OrderId = highestOrder.Id,
            BankId = bank.Id,
            Amount = 30000,
            IsVerified = false,
            CreatedAt = paymentToday,
            UpdatedAt = paymentToday,
        });
        await ctx.SaveChangesAsync();

        var repo = new OrderRepository(ctx, new FakeClock(new DateTime(2026, 8, 7, 17, 0, 0, DateTimeKind.Utc)));
        var result = await repo.SearchOrdersAsync(
            branchId: branch.Id,
            fromDate: new DateTime(2026, 8, 7, 5, 0, 0, DateTimeKind.Utc),
            toDate: new DateTime(2026, 8, 8, 4, 59, 59, 999, DateTimeKind.Utc),
            bankId: bank.Id,
            transferVerificationMode: true,
            page: 1,
            pageSize: 10,
            cancellationToken: default);

        Assert.Equal(2, result.Items.Count());
        Assert.Equal(highestOrder.Id, result.Items.First().Id);
        Assert.Equal(firstOrder.Id, result.Items.Skip(1).Single().Id);
    }
}
