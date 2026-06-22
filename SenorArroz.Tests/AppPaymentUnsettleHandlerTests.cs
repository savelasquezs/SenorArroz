using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class AppPaymentUnsettleHandlerTests
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

    private static (Branch Branch, Bank Bank, App App, User User) SeedBase(ApplicationDbContext db, DateTime utcNow)
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
            BranchId = branch.Id,
            Name = "Banco",
            Active = true,
            Branch = branch,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

        var app = new App
        {
            Id = 1,
            BankId = bank.Id,
            Name = "Rappi",
            Active = true,
            Bank = bank,
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

        db.Branches.Add(branch);
        db.Banks.Add(bank);
        db.Apps.Add(app);
        db.Users.Add(user);
        return (branch, bank, app, user);
    }

    private static Order CreateOrder(int id, Branch branch, User user, DateTime utcNow) =>
        new()
        {
            Id = id,
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Delivered,
            Type = OrderType.Delivery,
            Total = 1000,
            StatusTimes = "{}",
            Branch = branch,
            TakenBy = user,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

    private static UnsettleAppPaymentHandler BuildHandler(ApplicationDbContext db) =>
        new(new AppPaymentRepository(db), db, new TestCurrentUser());

    [Fact]
    public async Task Unsettle_single_app_settlement_deletes_bank_payment_and_marks_app_pending()
    {
        var utcNow = new DateTime(2026, 6, 18, 16, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Unsettle_single_app_settlement_deletes_bank_payment_and_marks_app_pending));
        var (branch, bank, app, user) = SeedBase(db, utcNow);
        var order = CreateOrder(10, branch, user, utcNow);

        db.Orders.Add(order);
        db.AppPayments.Add(new AppPayment
        {
            Id = 20,
            OrderId = order.Id,
            AppId = app.Id,
            Amount = 1000m,
            IsSetted = true,
            Order = order,
            App = app,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        });
        db.BankPayments.Add(new BankPayment
        {
            Id = 30,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 1000m,
            IsAppSettlement = true,
            AppSettlementSourcePaymentIds = "[20]",
            Order = order,
            Bank = bank,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        });
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).Handle(new UnsettleAppPaymentCommand { Id = 20 }, CancellationToken.None);

        Assert.True(result);
        Assert.False(await db.AppPayments.Where(ap => ap.Id == 20).Select(ap => ap.IsSetted).SingleAsync());
        Assert.False(await db.BankPayments.AnyAsync(bp => bp.Id == 30));
    }

    [Fact]
    public async Task Unsettle_one_payment_from_aggregate_settlement_reduces_bank_payment()
    {
        var utcNow = new DateTime(2026, 6, 18, 16, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Unsettle_one_payment_from_aggregate_settlement_reduces_bank_payment));
        var (branch, bank, app, user) = SeedBase(db, utcNow);
        var orderA = CreateOrder(10, branch, user, utcNow);
        var orderB = CreateOrder(11, branch, user, utcNow);

        db.Orders.AddRange(orderA, orderB);
        db.AppPayments.AddRange(
            new AppPayment
            {
                Id = 20,
                OrderId = orderA.Id,
                AppId = app.Id,
                Amount = 1000m,
                IsSetted = true,
                Order = orderA,
                App = app,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
            },
            new AppPayment
            {
                Id = 21,
                OrderId = orderB.Id,
                AppId = app.Id,
                Amount = 2000m,
                IsSetted = true,
                Order = orderB,
                App = app,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
            });
        db.BankPayments.Add(new BankPayment
        {
            Id = 30,
            OrderId = orderA.Id,
            BankId = bank.Id,
            Amount = 3000m,
            IsAppSettlement = true,
            AppSettlementSourcePaymentIds = "[20,21]",
            Order = orderA,
            Bank = bank,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        });
        await db.SaveChangesAsync();

        var result = await BuildHandler(db).Handle(new UnsettleAppPaymentCommand { Id = 20 }, CancellationToken.None);

        Assert.True(result);
        Assert.False(await db.AppPayments.Where(ap => ap.Id == 20).Select(ap => ap.IsSetted).SingleAsync());
        Assert.True(await db.AppPayments.Where(ap => ap.Id == 21).Select(ap => ap.IsSetted).SingleAsync());

        var bankPayment = await db.BankPayments.SingleAsync(bp => bp.Id == 30);
        Assert.Equal(2000m, bankPayment.Amount);
        Assert.Equal("[21]", bankPayment.AppSettlementSourcePaymentIds);
    }

    [Fact]
    public async Task Unsettle_verified_app_settlement_does_not_change_app_or_bank()
    {
        var utcNow = new DateTime(2026, 6, 18, 16, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Unsettle_verified_app_settlement_does_not_change_app_or_bank));
        var (branch, bank, app, user) = SeedBase(db, utcNow);
        var order = CreateOrder(10, branch, user, utcNow);

        db.Orders.Add(order);
        db.AppPayments.Add(new AppPayment
        {
            Id = 20,
            OrderId = order.Id,
            AppId = app.Id,
            Amount = 1000m,
            IsSetted = true,
            Order = order,
            App = app,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        });
        db.BankPayments.Add(new BankPayment
        {
            Id = 30,
            OrderId = order.Id,
            BankId = bank.Id,
            Amount = 1000m,
            IsAppSettlement = true,
            AppSettlementSourcePaymentIds = "[20]",
            IsVerified = true,
            Order = order,
            Bank = bank,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() =>
            BuildHandler(db).Handle(new UnsettleAppPaymentCommand { Id = 20 }, CancellationToken.None));

        Assert.True(await db.AppPayments.Where(ap => ap.Id == 20).Select(ap => ap.IsSetted).SingleAsync());
        var bankPayment = await db.BankPayments.SingleAsync(bp => bp.Id == 30);
        Assert.Equal(1000m, bankPayment.Amount);
    }
}
