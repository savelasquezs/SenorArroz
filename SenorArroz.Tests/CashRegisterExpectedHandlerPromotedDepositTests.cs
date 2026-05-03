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
}
