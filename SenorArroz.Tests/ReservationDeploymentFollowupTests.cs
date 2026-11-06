using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ReservationDeposits.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

public sealed class ReservationDeploymentFollowupTests
{
    private sealed class Actor : ICurrentUser
    {
        public int Id => 1;
        public int TenantId => 1;
        public int BranchId => 1;
        public string Role => Roles.Cashier;
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
    }

    private static async Task<(Order Order, App App, User User)> SeedAsync(ApplicationDbContext db)
    {
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Santander", Address = "Test", Phone1 = "300" };
        var user = new User
        {
            Id = 1, TenantId = 1, BranchId = 1, Name = "Caja",
            Email = "caja@test.local", PasswordHash = "x", Role = UserRole.Cashier,
        };
        var bank = new Bank { Id = 1, TenantId = 1, BranchId = 1, Name = "Banco", Active = true };
        var app = new App { Id = 1, TenantId = 1, BankId = 1, Name = "Nequi", Active = true };
        var order = new Order
        {
            Id = 10, TenantId = 1, BranchId = 1, TakenById = 1, OriginBranchId = 1,
            Type = OrderType.Delivery, Status = OrderStatus.Ready, StatusTimes = "{}",
            ReservedFor = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc),
            Total = 100_000,
        };
        db.Branches.Add(branch);
        db.Users.Add(user);
        db.Banks.Add(bank);
        db.Apps.Add(app);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (order, app, user);
    }

    [Fact]
    public async Task App_receipt_can_be_corrected_and_removed_after_reservation_became_ready_delivery()
    {
        await using var db = CreateDb();
        var (order, app, user) = await SeedAsync(db);
        var deposit = new ReservationDeposit
        {
            TenantId = 1, OrderId = order.Id, BranchId = 1,
            Amount = 30_000m, AppId = app.Id,
            ReceivedAt = new DateTime(2026, 11, 6, 16, 0, 0, DateTimeKind.Utc),
            ReceivedById = user.Id,
        };
        db.ReservationDeposits.Add(deposit);
        await db.SaveChangesAsync();
        db.AppPayments.Add(new AppPayment
        {
            TenantId = 1, OrderId = order.Id, AppId = app.Id,
            Amount = 30_000m, SourceReservationDepositId = deposit.Id,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var update = new UpdateReservationDepositHandler(
            db, new ReservationDepositRepository(db), new TestBranchContext(1), new Actor());
        await update.Handle(new UpdateReservationDepositCommand { Id = deposit.Id, Amount = 25_000m },
            CancellationToken.None);

        Assert.Equal(25_000m, (await db.ReservationDeposits.AsNoTracking().SingleAsync()).Amount);
        Assert.Equal(25_000m, (await db.AppPayments.AsNoTracking().SingleAsync()).Amount);

        var delete = new DeleteReservationDepositHandler(db, new Actor(), new TestBranchContext(1));
        await delete.Handle(new DeleteReservationDepositCommand { Id = deposit.Id }, CancellationToken.None);
        Assert.Empty(await db.ReservationDeposits.ToListAsync());
        Assert.Empty(await db.AppPayments.ToListAsync());
    }
}
