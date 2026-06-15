using Microsoft.EntityFrameworkCore;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class BankLedgerReservationDepositTests
{
    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts);
    }

    [Fact]
    public async Task Bank_ledger_counts_reservation_deposit_and_ignores_promoted_bank_payment_duplicates()
    {
        var utcNow = new DateTime(2026, 6, 14, 15, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Bank_ledger_counts_reservation_deposit_and_ignores_promoted_bank_payment_duplicates));

        var branch = new Branch
        {
            Id = 1,
            Name = "Sucursal",
            Address = "A",
            Phone1 = "1",
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        var bank = new Bank
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Banco",
            Active = true,
            Branch = branch,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        var user = new User
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Caja",
            Email = "caja@test.com",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        var order = new Order
        {
            Id = 10,
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Delivery,
            Total = 100000,
            Branch = branch,
            TakenBy = user,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            StatusTimes = "{}",
        };
        var deposit = new ReservationDeposit
        {
            Id = 20,
            OrderId = order.Id,
            BranchId = branch.Id,
            Amount = 40000m,
            IsEffective = false,
            BankId = bank.Id,
            ReceivedAt = utcNow.AddHours(-2),
            ReceivedById = user.Id,
            Order = order,
            Branch = branch,
            Bank = bank,
            ReceivedBy = user,
            CreatedAt = utcNow.AddHours(-2),
            UpdatedAt = utcNow.AddHours(-2),
        };

        db.Branches.Add(branch);
        db.Banks.Add(bank);
        db.Users.Add(user);
        db.Orders.Add(order);
        db.ReservationDeposits.Add(deposit);
        db.BankPayments.Add(new BankPayment
        {
            Id = 30,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 40000m,
            SourceReservationDepositId = deposit.Id,
            Order = order,
            Bank = bank,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
        });
        db.BankPayments.Add(new BankPayment
        {
            Id = 31,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 15000m,
            Order = order,
            Bank = bank,
            CreatedAt = utcNow.AddMinutes(-30),
            UpdatedAt = utcNow.AddMinutes(-30),
        });
        db.BankPayments.Add(new BankPayment
        {
            Id = 32,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 40000m,
            Order = order,
            Bank = bank,
            CreatedAt = utcNow.AddMinutes(-20),
            UpdatedAt = utcNow.AddMinutes(-20),
        });
        await db.SaveChangesAsync();

        var repo = new BankRepository(db);
        var ledger = new BankLedgerService(repo);

        var result = await ledger.GetRunningBalanceBreakdownAsync(bank.Id);

        Assert.Equal(15000m, result.BankPaymentsIn);
        Assert.Equal(40000m, result.ReservationDepositsIn);
        Assert.Equal(55000m, result.NetBalance);
        Assert.Equal(55000m, await repo.GetCurrentBalanceAsync(bank.Id));
    }
}
