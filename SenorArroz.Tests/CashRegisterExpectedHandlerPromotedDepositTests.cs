using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

public class CashRegisterExpectedHandlerPromotedDepositTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        public int Id => 1;
        public string Role => Roles.Cashier;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static (Branch Branch, Bank Bank, User User) SeedBase(ApplicationDbContext db, DateTime utcNow)
    {
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
            BranchId = 1,
            Name = "Banco",
            Active = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Branch = branch,
        };

        var user = new User
        {
            Id = 1,
            BranchId = 1,
            Name = "Caja",
            Email = "caja@test.com",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

        db.Branches.Add(branch);
        db.Banks.Add(bank);
        db.Users.Add(user);
        return (branch, bank, user);
    }

    private static GetCashRegisterExpectedHandler BuildHandler(
        ApplicationDbContext db,
        Bank bank,
        DateTime utcNow,
        CashRegisterClosure? lastClosure = null)
    {
        var closureRepo = new Mock<ICashRegisterClosureRepository>();
        closureRepo.Setup(r => r.GetLastByBranchAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(lastClosure);

        var bankRepo = new Mock<IBankRepository>();
        bankRepo.Setup(r => r.GetByBranchIdAsync(1, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { bank }.AsEnumerable());

        return new GetCashRegisterExpectedHandler(
            closureRepo.Object,
            bankRepo.Object,
            db,
            new TestCurrentUser(),
            new FakeClock(utcNow));
    }

    [Fact]
    public async Task Promoted_reservation_deposit_is_counted_once_in_bank_expected_balance()
    {
        var utcNow = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Promoted_reservation_deposit_is_counted_once_in_bank_expected_balance));

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
            BranchId = 1,
            Name = "Banco",
            Active = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Branch = branch,
        };

        var order = new Order
        {
            Id = 10,
            BranchId = 1,
            TakenById = 1,
            Status = OrderStatus.Taken,
            Type = OrderType.Reservation,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
            PrepareAt = utcNow.AddHours(-1),
            StatusTimes = "{}",
            Branch = branch,
            TakenBy = new User
            {
                Id = 1,
                BranchId = 1,
                Name = "Caja",
                Email = "caja@test.com",
                Phone = "1",
                PasswordHash = "x",
                Branch = branch,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
            },
        };

        var deposit = new ReservationDeposit
        {
            Id = 20,
            OrderId = order.Id,
            BranchId = 1,
            Amount = 1000m,
            IsEffective = false,
            BankId = bank.Id,
            ReceivedAt = utcNow.AddHours(-1),
            ReceivedById = 1,
            Order = order,
            Bank = bank,
        };

        var bankPayment = new BankPayment
        {
            Id = 30,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 1000m,
            SourceReservationDepositId = deposit.Id,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
            Order = order,
            Bank = bank,
        };

        db.Branches.Add(branch);
        db.Banks.Add(bank);
        db.Users.Add(order.TakenBy);
        db.Orders.Add(order);
        db.ReservationDeposits.Add(deposit);
        db.BankPayments.Add(bankPayment);
        await db.SaveChangesAsync();

        var closureRepo = new Mock<ICashRegisterClosureRepository>();
        closureRepo.Setup(r => r.GetLastByBranchAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((CashRegisterClosure?)null);

        var bankRepo = new Mock<IBankRepository>();
        bankRepo.Setup(r => r.GetByBranchIdAsync(1, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { bank }.AsEnumerable());

        var handler = new GetCashRegisterExpectedHandler(
            closureRepo.Object,
            bankRepo.Object,
            db,
            new TestCurrentUser(),
            new FakeClock(utcNow));

        var result = await handler.Handle(new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankResult = Assert.Single(result.Banks);
        Assert.Equal(1000m, bankResult.ExpectedBalance);
    }

    [Fact]
    public async Task Promoted_reservation_deposit_received_before_last_closure_does_not_increase_bank_or_global_expected()
    {
        var utcNow = new DateTime(2026, 5, 9, 23, 0, 0, DateTimeKind.Utc);
        var since = utcNow.AddHours(-6);
        using var db = CreateCtx(nameof(Promoted_reservation_deposit_received_before_last_closure_does_not_increase_bank_or_global_expected));
        var (branch, bank, user) = SeedBase(db, utcNow);

        var order = new Order
        {
            Id = 1613,
            BranchId = 1,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Delivery,
            Total = 170000,
            CreatedAt = since.AddDays(-1),
            UpdatedAt = utcNow.AddHours(-1),
            PrepareAt = utcNow.AddHours(-2),
            StatusTimes = "{}",
            Branch = branch,
            TakenBy = user,
        };

        var deposit = new ReservationDeposit
        {
            Id = 7,
            OrderId = order.Id,
            BranchId = 1,
            Amount = 170000m,
            IsEffective = false,
            BankId = bank.Id,
            ReceivedAt = since.AddHours(-1),
            ReceivedById = user.Id,
            Order = order,
            Bank = bank,
            ReceivedBy = user,
            Branch = branch,
        };

        var promotedPayment = new BankPayment
        {
            Id = 744,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 170000m,
            SourceReservationDepositId = deposit.Id,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
            Order = order,
            Bank = bank,
        };

        db.Orders.Add(order);
        db.ReservationDeposits.Add(deposit);
        db.BankPayments.Add(promotedPayment);
        await db.SaveChangesAsync();

        var lastClosure = new CashRegisterClosure
        {
            Id = 1,
            BranchId = 1,
            ClosedAt = since,
            ClosingCash = 0,
            BankReconciliations =
            {
                new CashClosureBankReconciliation
                {
                    BankId = bank.Id,
                    ActualBalance = 500000m,
                    ExpectedBalance = 500000m,
                },
            },
        };

        var handler = BuildHandler(db, bank, utcNow, lastClosure);

        var result = await handler.Handle(new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankResult = Assert.Single(result.Banks);
        Assert.Equal(500000m, bankResult.ExpectedBalance);
        Assert.Equal(0m, result.SalesInPeriodTotal);
        Assert.Equal(0m, result.ReservationDepositsAddedToGlobalTotal);
        Assert.Equal(500000m, result.ExpectedGlobalTotal);
    }

    [Fact]
    public async Task Normal_bank_payment_without_source_reservation_deposit_still_increases_bank_expected()
    {
        var utcNow = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Normal_bank_payment_without_source_reservation_deposit_still_increases_bank_expected));
        var (branch, bank, user) = SeedBase(db, utcNow);

        var order = new Order
        {
            Id = 11,
            BranchId = 1,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Delivery,
            Total = 25000,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
            PrepareAt = utcNow.AddHours(-1),
            StatusTimes = "{}",
            Branch = branch,
            TakenBy = user,
        };

        db.Orders.Add(order);
        db.BankPayments.Add(new BankPayment
        {
            Id = 31,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 25000m,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
            Order = order,
            Bank = bank,
        });
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, bank, utcNow);

        var result = await handler.Handle(new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankResult = Assert.Single(result.Banks);
        Assert.Equal(25000m, bankResult.ExpectedBalance);
        Assert.Equal(25000m, result.SalesInPeriodTotal);
    }

    [Fact]
    public async Task Reservation_deposit_received_today_for_future_order_increases_bank_and_global_expected_by_received_at()
    {
        var utcNow = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Reservation_deposit_received_today_for_future_order_increases_bank_and_global_expected_by_received_at));
        var (branch, bank, user) = SeedBase(db, utcNow);

        var order = new Order
        {
            Id = 12,
            BranchId = 1,
            TakenById = user.Id,
            Status = OrderStatus.Taken,
            Type = OrderType.Reservation,
            Total = 40000,
            CreatedAt = utcNow.AddHours(-1),
            UpdatedAt = utcNow.AddHours(-1),
            PrepareAt = utcNow.AddDays(1),
            ReservedFor = utcNow.AddDays(1).AddHours(1),
            StatusTimes = "{}",
            Branch = branch,
            TakenBy = user,
        };

        db.Orders.Add(order);
        db.ReservationDeposits.Add(new ReservationDeposit
        {
            Id = 21,
            OrderId = order.Id,
            BranchId = 1,
            Amount = 10000m,
            IsEffective = false,
            BankId = bank.Id,
            ReceivedAt = utcNow.AddHours(-1),
            ReceivedById = user.Id,
            Order = order,
            Bank = bank,
            ReceivedBy = user,
            Branch = branch,
        });
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, bank, utcNow);

        var result = await handler.Handle(new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankResult = Assert.Single(result.Banks);
        Assert.Equal(10000m, bankResult.ExpectedBalance);
        Assert.Equal(10000m, result.ReservationDepositsAddedToGlobalTotal);
        Assert.Equal(10000m, result.ExpectedGlobalTotal);
    }

    [Fact]
    public async Task Cashier_expected_global_excludes_cash_vault_balance_from_last_closure()
    {
        var utcNow = new DateTime(2026, 6, 13, 15, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Cashier_expected_global_excludes_cash_vault_balance_from_last_closure));
        var (branch, normalBank, _) = SeedBase(db, utcNow);

        var cashVaultBank = new Bank
        {
            Id = 2,
            BranchId = 1,
            Name = "Caja mayor",
            Type = BankType.CashVault,
            Active = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Branch = branch,
        };

        db.Banks.Add(cashVaultBank);
        await db.SaveChangesAsync();

        var lastClosure = new CashRegisterClosure
        {
            Id = 1,
            BranchId = 1,
            ClosedAt = utcNow.AddHours(-2),
            ClosingCash = 0,
            BankReconciliations =
            {
                new CashClosureBankReconciliation
                {
                    BankId = normalBank.Id,
                    ExpectedBalance = 0,
                    ActualBalance = 0,
                },
                new CashClosureBankReconciliation
                {
                    BankId = cashVaultBank.Id,
                    ExpectedBalance = 10_000_000m,
                    ActualBalance = 10_000_000m,
                },
            },
        };

        var closureRepo = new Mock<ICashRegisterClosureRepository>();
        closureRepo.Setup(r => r.GetLastByBranchAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(lastClosure);

        var bankRepo = new Mock<IBankRepository>();
        bankRepo.Setup(r => r.GetByBranchIdAsync(1, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { normalBank, cashVaultBank }.AsEnumerable());

        var handler = new GetCashRegisterExpectedHandler(
            closureRepo.Object,
            bankRepo.Object,
            db,
            new TestCurrentUser(),
            new FakeClock(utcNow));

        var result = await handler.Handle(new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var visibleBank = Assert.Single(result.Banks);
        Assert.Equal(normalBank.Id, visibleBank.BankId);
        Assert.Equal(0m, result.OpeningGlobalTotal);
        Assert.Equal(0m, result.ExpectedGlobalTotal);

        var carriedHiddenBank = Assert.Single(result.HiddenBanksForClosureCarry);
        Assert.Equal(cashVaultBank.Id, carriedHiddenBank.BankId);
        Assert.Equal(10_000_000m, carriedHiddenBank.ExpectedBalance);
    }
}
