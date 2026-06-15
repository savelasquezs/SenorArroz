using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class CashVaultMovementHistoryTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        public int Id => 1;
        public string Role => Roles.Admin;
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
    public async Task Get_cash_vault_movements_returns_only_cash_vault_history_in_descending_order()
    {
        var now = new DateTime(2026, 6, 15, 15, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Get_cash_vault_movements_returns_only_cash_vault_history_in_descending_order));

        var branch = new Branch
        {
            Id = 1,
            Name = "Sucursal",
            Address = "A",
            Phone1 = "1",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var user = new User
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Admin Caja",
            Email = "admin@test.com",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var vault = new Bank
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Caja Mayor Efectivo",
            Type = BankType.CashVault,
            Active = true,
            Branch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var regularBank = new Bank
        {
            Id = 2,
            BranchId = branch.Id,
            Name = "Banco Normal",
            Type = BankType.Normal,
            Active = true,
            Branch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Branches.Add(branch);
        db.Users.Add(user);
        db.Banks.AddRange(vault, regularBank);
        db.CashVaultMovements.AddRange(
            new CashVaultMovement
            {
                Id = 1,
                BranchId = branch.Id,
                BankId = vault.Id,
                Kind = CashVaultMovementKind.AbonoToVault,
                Amount = 100000m,
                Note = "Abono inicial",
                CreatedById = user.Id,
                Branch = branch,
                Bank = vault,
                CreatedBy = user,
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-2),
            },
            new CashVaultMovement
            {
                Id = 2,
                BranchId = branch.Id,
                BankId = vault.Id,
                Kind = CashVaultMovementKind.WithdrawFromVault,
                Amount = 25000m,
                CreatedById = user.Id,
                Branch = branch,
                Bank = vault,
                CreatedBy = user,
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            },
            new CashVaultMovement
            {
                Id = 3,
                BranchId = branch.Id,
                BankId = regularBank.Id,
                Kind = CashVaultMovementKind.AbonoToVault,
                Amount = 999999m,
                CreatedById = user.Id,
                Branch = branch,
                Bank = regularBank,
                CreatedBy = user,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync();

        var handler = new GetCashVaultMovementsHandler(db, new TestCurrentUser());

        var result = await handler.Handle(new GetCashVaultMovementsQuery { BranchId = branch.Id }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        var items = result.Items.ToList();
        Assert.Equal([2, 1], items.Select(i => i.Id).ToArray());
        Assert.All(items, item => Assert.Equal(vault.Id, item.BankId));
        Assert.Equal("Admin Caja", items[0].CreatedByName);
    }
}
