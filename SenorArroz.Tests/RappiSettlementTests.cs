using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class RappiSettlementTests
{
    private sealed class CurrentUser(string role = Roles.Admin) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    [Fact]
    public async Task Settlement_prorates_actual_deposit_by_expected_net_and_assigns_residue()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(Settlement_prorates_actual_deposit_by_expected_net_and_assigns_residue))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ApplicationDbContext(options);
        var branch = new Branch { Id = 1, Name = "Santander", Address = "-", Phone1 = "-" };
        var user = new User
        {
            Id = 1,
            BranchId = 1,
            Name = "Administrador",
            Email = "admin@test.local",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch
        };
        var bank = new Bank { Id = 1, BranchId = 1, Name = "Banco", Active = true, Branch = branch };
        var app = new App { Id = 1, BankId = 1, Name = "Rappi", Active = true, Bank = bank };
        var order1 = NewOrder(1, branch, user);
        var order2 = NewOrder(2, branch, user);
        var payment1 = NewPayment(1, order1, app, 100m, 75m);
        var payment2 = NewPayment(2, order2, app, 200m, 150m);

        db.AddRange(branch, user, bank, app, order1, order2, payment1, payment2);
        await db.SaveChangesAsync();

        var handler = new SettleMultipleAppPaymentsHandler(
            db,
            new CurrentUser(Roles.Cashier),
            new TestBranchContext());
        await handler.Handle(new SettleMultipleAppPaymentsCommand
        {
            PaymentIds = [1, 2],
            ActualAmount = 210m
        }, CancellationToken.None);

        Assert.True(payment1.IsSetted);
        Assert.True(payment2.IsSetted);
        Assert.Equal(70m, payment1.ActualSettledAmount);
        Assert.Equal(140m, payment2.ActualSettledAmount);
        Assert.Equal(-5m, payment1.SettlementVariance);
        Assert.Equal(-10m, payment2.SettlementVariance);

        var bankPayment = await db.BankPayments.SingleAsync();
        Assert.Equal(210m, bankPayment.Amount);
        Assert.True(bankPayment.IsAppSettlement);
        Assert.Equal("[1,2]", bankPayment.AppSettlementSourcePaymentIds);
    }

    [Fact]
    public void Cashier_can_settle_but_cannot_unsettle_app_payments()
    {
        var singleRoles = typeof(AppPaymentsController)
            .GetMethod(nameof(AppPaymentsController.SettleAppPayment))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single().Roles ?? string.Empty;
        var multipleRoles = typeof(AppPaymentsController)
            .GetMethod(nameof(AppPaymentsController.SettleMultipleAppPayments))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single().Roles ?? string.Empty;
        var unsettleRoles = typeof(AppPaymentsController)
            .GetMethod(nameof(AppPaymentsController.UnsettleAppPayment))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single().Roles ?? string.Empty;

        Assert.Contains("Cashier", singleRoles);
        Assert.Contains("Cashier", multipleRoles);
        Assert.DoesNotContain("Cashier", unsettleRoles);
    }

    private static Order NewOrder(int id, Branch branch, User user) => new()
    {
        Id = id,
        BranchId = branch.Id,
        TakenById = user.Id,
        Status = OrderStatus.Delivered,
        Type = OrderType.Delivery,
        Total = 100,
        StatusTimes = "{}",
        Branch = branch,
        TakenBy = user
    };

    private static AppPayment NewPayment(
        int id,
        Order order,
        App app,
        decimal gross,
        decimal expectedNet) => new()
    {
        Id = id,
        OrderId = order.Id,
        AppId = app.Id,
        Amount = gross,
        ExpectedNetAmount = expectedNet,
        Order = order,
        App = app
    };
}
