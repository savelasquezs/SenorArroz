using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.Commands;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class UpdateExpenseHeaderBankPaymentsTests
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

    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<ExpenseHeaderMappingProfile>();
            cfg.AddProfile<ExpenseMappingProfile>();
            cfg.AddProfile<ExpenseCategoryMappingProfile>();
        }, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task Update_with_null_bank_payments_removes_existing_expense_bank_payments()
    {
        var now = new DateTime(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Update_with_null_bank_payments_removes_existing_expense_bank_payments));

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
            Name = "Caja",
            Email = "caja@test.com",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var supplier = new Supplier
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Proveedor",
            Phone = "1",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var category = new ExpenseCategory
        {
            Id = 1,
            Name = "Categoria",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var expense = new Expense
        {
            Id = 1,
            Name = "Insumo",
            CategoryId = category.Id,
            Category = category,
            Unit = ExpenseUnit.Unit,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var bank = new Bank
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Bancolombia",
            Active = true,
            Branch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var header = new ExpenseHeader
        {
            Id = 1,
            BranchId = branch.Id,
            SupplierId = supplier.Id,
            CreatedById = user.Id,
            Total = 1000m,
            Branch = branch,
            Supplier = supplier,
            CreatedBy = user,
            CreatedAt = now,
            UpdatedAt = now,
            ExpenseDetails =
            {
                new ExpenseDetail
                {
                    Id = 1,
                    ExpenseId = expense.Id,
                    Expense = expense,
                    Quantity = 1,
                    Amount = 1000,
                    Total = 1000m,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            },
            ExpenseBankPayments =
            {
                new ExpenseBankPayment
                {
                    Id = 1,
                    BankId = bank.Id,
                    Bank = bank,
                    Amount = 1000m,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            },
        };

        db.Branches.Add(branch);
        db.Users.Add(user);
        db.Suppliers.Add(supplier);
        db.ExpenseCategories.Add(category);
        db.Expenses.Add(expense);
        db.Banks.Add(bank);
        db.ExpenseHeaders.Add(header);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateExpenseHeaderHandler(
            new ExpenseHeaderRepository(db),
            new BankRepository(db),
            db,
            CreateMapper(),
            new TestCurrentUser(),
            new FakeClock(now));

        var result = await handler.Handle(new UpdateExpenseHeaderCommand
        {
            Id = header.Id,
            ExpenseHeader = new UpdateExpenseHeaderDto
            {
                SupplierId = supplier.Id,
                IncludeVat = false,
                ExpenseBankPayments = null,
                ExpenseDetails = new List<UpdateExpenseDetailDto>
                {
                    new()
                    {
                        Id = 1,
                        ExpenseId = expense.Id,
                        Quantity = 1,
                        Amount = 1000,
                        Total = 1000m,
                    },
                },
            },
        }, CancellationToken.None);

        Assert.Empty(result.ExpenseBankPayments);
        Assert.Empty(await db.ExpenseBankPayments.Where(p => p.ExpenseHeaderId == header.Id).ToListAsync());
    }

    [Fact]
    public async Task Update_with_deliveryman_id_associates_existing_expense_to_deliveryman()
    {
        var now = new DateTime(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Update_with_deliveryman_id_associates_existing_expense_to_deliveryman));

        var branch = new Branch
        {
            Id = 1,
            Name = "Sucursal",
            Address = "A",
            Phone1 = "1",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var cashier = new User
        {
            Id = 1,
            BranchId = branch.Id,
            Role = UserRole.Cashier,
            Name = "Caja",
            Email = "caja@test.com",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var deliveryman = new User
        {
            Id = 2,
            BranchId = branch.Id,
            Role = UserRole.Deliveryman,
            Name = "Domi",
            Email = "domi@test.com",
            Phone = "2",
            PasswordHash = "x",
            Branch = branch,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var supplier = new Supplier
        {
            Id = 1,
            BranchId = branch.Id,
            Name = "Proveedor",
            Phone = "1",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var category = new ExpenseCategory
        {
            Id = 1,
            Name = "Categoria",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var expense = new Expense
        {
            Id = 1,
            Name = "Insumo",
            CategoryId = category.Id,
            Category = category,
            Unit = ExpenseUnit.Unit,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var header = new ExpenseHeader
        {
            Id = 1,
            BranchId = branch.Id,
            SupplierId = supplier.Id,
            CreatedById = cashier.Id,
            Total = 1000m,
            Branch = branch,
            Supplier = supplier,
            CreatedBy = cashier,
            CreatedAt = now,
            UpdatedAt = now,
            ExpenseDetails =
            {
                new ExpenseDetail
                {
                    Id = 1,
                    ExpenseId = expense.Id,
                    Expense = expense,
                    Quantity = 1,
                    Amount = 1000,
                    Total = 1000m,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            },
        };

        db.Branches.Add(branch);
        db.Users.AddRange(cashier, deliveryman);
        db.Suppliers.Add(supplier);
        db.ExpenseCategories.Add(category);
        db.Expenses.Add(expense);
        db.ExpenseHeaders.Add(header);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new UpdateExpenseHeaderHandler(
            new ExpenseHeaderRepository(db),
            new BankRepository(db),
            db,
            CreateMapper(),
            new TestCurrentUser(),
            new FakeClock(now));

        var result = await handler.Handle(new UpdateExpenseHeaderCommand
        {
            Id = header.Id,
            ExpenseHeader = new UpdateExpenseHeaderDto
            {
                SupplierId = supplier.Id,
                DeliverymanId = deliveryman.Id,
                IncludeVat = false,
                ExpenseBankPayments = new List<CreateExpenseBankPaymentDto>(),
                ExpenseDetails = new List<UpdateExpenseDetailDto>
                {
                    new()
                    {
                        Id = 1,
                        ExpenseId = expense.Id,
                        Quantity = 1,
                        Amount = 1000,
                        Total = 1000m,
                    },
                },
            },
        }, CancellationToken.None);

        Assert.Equal(deliveryman.Id, result.DeliverymanId);
        Assert.Equal("Domi", result.DeliverymanName);

        var saved = await db.ExpenseHeaders.AsNoTracking().SingleAsync(e => e.Id == header.Id);
        Assert.Equal(deliveryman.Id, saved.DeliverymanId);
    }
}
