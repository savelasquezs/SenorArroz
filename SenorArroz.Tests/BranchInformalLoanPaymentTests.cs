using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.Commands;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Queries;
using SenorArroz.Application.Features.ExpenseHeaders.Commands;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using AutoMapper;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public class BranchInformalLoanPaymentTests
{
    private sealed class CurrentUser(string role = Roles.Admin) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(
            options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
    }

    private static async Task<(Branch Branch, Bank Bank, User User)> SeedBankContext(
        ApplicationDbContext db,
        DateTime now)
    {
        var branch = new Branch { Id = 1, Name = "Sucursal", Address = "A", Phone1 = "1", CreatedAt = now, UpdatedAt = now };
        var bank = new Bank
        {
            Id = 1, BranchId = 1, Branch = branch, Name = "Bancolombia", Active = true,
            Type = BankType.Normal, CreatedAt = now, UpdatedAt = now
        };
        var user = new User
        {
            Id = 1, BranchId = 1, Branch = branch, Name = "Admin", Email = "admin@test.com", Phone = "1",
            PasswordHash = "x", Role = UserRole.Admin, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(branch, bank, user);
        await db.SaveChangesAsync();
        return (branch, bank, user);
    }

    private static GetCashRegisterExpectedHandler BuildExpectedHandler(
        ApplicationDbContext db,
        Bank bank,
        DateTime now,
        CashRegisterClosure? lastClosure = null)
    {
        var closureRepository = new Mock<ICashRegisterClosureRepository>();
        closureRepository.Setup(repository => repository.GetLastByBranchAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastClosure);
        var bankRepository = new Mock<IBankRepository>();
        bankRepository.Setup(repository => repository.GetByBranchIdAsync(1, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { bank }.AsEnumerable());
        return new GetCashRegisterExpectedHandler(
            closureRepository.Object,
            bankRepository.Object,
            db,
            new CurrentUser(),
            new FakeClock(now));
    }

    [Fact]
    public async Task Cash_payment_only_reduces_loan_and_records_history()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Cash_payment_only_reduces_loan_and_records_history));
        db.BranchInformalLoans.Add(new BranchInformalLoan
        {
            Id = 10,
            BranchId = 1,
            Concept = "Préstamo",
            Amount = 100000m,
            CreatedById = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var handler = new CreateBranchInformalLoanPaymentHandler(db, new CurrentUser(), new FakeClock(now.AddHours(1)));
        var result = await handler.Handle(new CreateBranchInformalLoanPaymentCommand
        {
            LoanId = 10,
            BranchId = 1,
            Dto = new CreateBranchInformalLoanPaymentDto { Amount = 40000m, Notes = "Primer abono" }
        }, CancellationToken.None);

        Assert.Equal(60000m, db.BranchInformalLoans.Single().Amount);
        Assert.Equal(100000m, result.BalanceBefore);
        Assert.Equal(60000m, result.BalanceAfter);
        Assert.Equal(BranchInformalLoanPaymentKind.Cash, result.Kind);
        Assert.Empty(db.ExpenseHeaders);
        Assert.Empty(db.CashVaultMovements);
    }

    [Fact]
    public async Task Final_payment_closes_loan_automatically()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Final_payment_closes_loan_automatically));
        db.BranchInformalLoans.Add(new BranchInformalLoan
        {
            Id = 11,
            BranchId = 1,
            Concept = "Préstamo",
            Amount = 50000m,
            CreatedById = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var handler = new CreateBranchInformalLoanPaymentHandler(db, new CurrentUser(), new FakeClock(now.AddHours(1)));
        await handler.Handle(new CreateBranchInformalLoanPaymentCommand
        {
            LoanId = 11,
            BranchId = 1,
            Dto = new CreateBranchInformalLoanPaymentDto { Amount = 50000m }
        }, CancellationToken.None);

        var loan = db.BranchInformalLoans.Single();
        Assert.Equal(0m, loan.Amount);
        Assert.Equal(now.AddHours(1), loan.DeactivatedAt);
        Assert.Equal("Pagado por completo", loan.DeactivationNotes);
    }

    [Fact]
    public async Task Cashier_cannot_read_loan_history()
    {
        await using var db = CreateContext(nameof(Cashier_cannot_read_loan_history));
        var handler = new GetBranchInformalLoanHistoryHandler(db, new CurrentUser(Roles.Cashier));

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new GetBranchInformalLoanHistoryQuery { BranchId = 1 },
            CancellationToken.None));
    }

    [Fact]
    public async Task Expense_payment_creates_expense_and_reduces_loan_atomically()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Expense_payment_creates_expense_and_reduces_loan_atomically));
        var branch = new Branch { Id = 1, Name = "Sucursal", Address = "A", Phone1 = "1", CreatedAt = now, UpdatedAt = now };
        var user = new User
        {
            Id = 1, BranchId = 1, Branch = branch, Name = "Admin", Email = "admin@test.com", Phone = "1",
            PasswordHash = "x", Role = UserRole.Admin, CreatedAt = now, UpdatedAt = now
        };
        var supplier = new Supplier { Id = 1, Name = "General", Phone = "1", CreatedAt = now, UpdatedAt = now };
        var category = new ExpenseCategory { Id = 1, Name = "Administrativos", CreatedAt = now, UpdatedAt = now };
        var expense = new Expense
        {
            Id = 1, Name = "Préstamo asumido", CategoryId = 1, Category = category,
            TracksInventory = false, CreatedAt = now, UpdatedAt = now
        };
        var loan = new BranchInformalLoan
        {
            Id = 20, BranchId = 1, Branch = branch, Concept = "Préstamo", Amount = 100000m,
            CreatedById = 1, CreatedBy = user, CreatedAt = now, UpdatedAt = now
        };
        db.AddRange(branch, user, supplier, category, expense, loan);
        await db.SaveChangesAsync();

        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<ExpenseHeaderDto>(It.IsAny<object>())).Returns(new ExpenseHeaderDto());
        var branchContext = new Mock<IBranchContext>();
        branchContext.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(1);
        var handler = new CreateExpenseHeaderHandler(
            new ExpenseHeaderRepository(db),
            Mock.Of<IBankRepository>(),
            db,
            mapper.Object,
            new CurrentUser(),
            branchContext.Object,
            new FakeClock(now.AddHours(1)));

        await handler.Handle(new CreateExpenseHeaderCommand
        {
            ExpenseHeader = new CreateExpenseHeaderDto
            {
                InformalLoanId = loan.Id,
                SupplierId = supplier.Id,
                ExpenseDetails =
                [
                    new CreateExpenseDetailDto { ExpenseId = expense.Id, Quantity = 1, Amount = 30000, Total = 30000m }
                ]
            }
        }, CancellationToken.None);

        Assert.Equal(70000m, db.BranchInformalLoans.Single().Amount);
        var payment = db.BranchInformalLoanPayments.Single();
        Assert.Equal(BranchInformalLoanPaymentKind.Expense, payment.Kind);
        Assert.Equal(30000m, payment.Amount);
        Assert.NotNull(payment.ExpenseHeaderId);
        Assert.Empty(db.ExpenseBankPayments);
    }

    [Fact]
    public async Task Only_bank_loans_reduce_bank_expected_balance()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Only_bank_loans_reduce_bank_expected_balance));
        var (_, bank, _) = await SeedBankContext(db, now);
        db.BranchInformalLoans.AddRange(
            new BranchInformalLoan { BranchId = 1, Concept = "Efectivo", Amount = 100000m, CreatedById = 1, CreatedAt = now, UpdatedAt = now },
            new BranchInformalLoan { BranchId = 1, BankId = bank.Id, Concept = "Banco", Amount = 300000m, CreatedById = 1, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var result = await BuildExpectedHandler(db, bank, now).Handle(
            new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankExpected = Assert.Single(result.Banks);
        Assert.Equal(-300000m, bankExpected.ExpectedBalance);
        Assert.Equal(300000m, bankExpected.InformalLoanDeduction);
    }

    [Fact]
    public async Task Direct_payment_recovers_bank_expected_but_expense_conversion_does_not()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Direct_payment_recovers_bank_expected_but_expense_conversion_does_not));
        var (_, bank, _) = await SeedBankContext(db, now);
        var loan = new BranchInformalLoan
        {
            Id = 20, BranchId = 1, BankId = bank.Id, Concept = "Banco", Amount = 100000m,
            CreatedById = 1, CreatedAt = now, UpdatedAt = now
        };
        db.BranchInformalLoans.Add(loan);
        db.BranchInformalLoanPayments.Add(new BranchInformalLoanPayment
        {
            LoanId = loan.Id, Loan = loan, Kind = BranchInformalLoanPaymentKind.Expense, Amount = 100000m,
            BalanceBefore = 200000m, BalanceAfter = 100000m, CreatedById = 1, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var result = await BuildExpectedHandler(db, bank, now).Handle(
            new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankExpected = Assert.Single(result.Banks);
        Assert.Equal(-200000m, bankExpected.ExpectedBalance);
        Assert.Equal(200000m, bankExpected.InformalLoanDeduction);
    }

    [Fact]
    public async Task Previous_closure_snapshot_prevents_double_bank_deduction()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Previous_closure_snapshot_prevents_double_bank_deduction));
        var (_, bank, _) = await SeedBankContext(db, now);
        db.BranchInformalLoans.Add(new BranchInformalLoan
        {
            BranchId = 1, BankId = bank.Id, Concept = "Banco", Amount = 200000m,
            CreatedById = 1, CreatedAt = now.AddDays(-2), UpdatedAt = now
        });
        var closure = new CashRegisterClosure
        {
            BranchId = 1, ClosedAt = now.AddHours(-2), CreatedById = 1, ClosingCash = 0,
            BankReconciliations =
            [
                new CashClosureBankReconciliation
                {
                    BankId = bank.Id, Bank = bank, ActualBalance = 700000m, ExpectedBalance = 700000m,
                    InformalLoanDeduction = 200000m, Adjustments = "[]"
                }
            ]
        };
        await db.SaveChangesAsync();

        var result = await BuildExpectedHandler(db, bank, now, closure).Handle(
            new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        var bankExpected = Assert.Single(result.Banks);
        Assert.Equal(700000m, bankExpected.ExpectedBalance);
        Assert.Equal(0m, bankExpected.InformalLoanAdjustment);
    }

    [Fact]
    public async Task Deactivating_bank_loan_assumes_remaining_balance_was_returned()
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(nameof(Deactivating_bank_loan_assumes_remaining_balance_was_returned));
        var (_, bank, _) = await SeedBankContext(db, now);
        db.BranchInformalLoans.Add(new BranchInformalLoan
        {
            BranchId = 1, BankId = bank.Id, Concept = "Banco", Amount = 300000m, CreatedById = 1,
            DeactivatedAt = now, DeactivatedById = 1, CreatedAt = now.AddDays(-1), UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var result = await BuildExpectedHandler(db, bank, now).Handle(
            new GetCashRegisterExpectedQuery { BranchId = 1 }, CancellationToken.None);

        Assert.Equal(0m, Assert.Single(result.Banks).ExpectedBalance);
    }

    [Theory]
    [InlineData(false, BankType.Normal, 1)]
    [InlineData(true, BankType.CashVault, 1)]
    [InlineData(true, BankType.RealVault, 1)]
    [InlineData(true, BankType.Normal, 2)]
    public async Task Creating_bank_loan_rejects_unavailable_or_foreign_banks(
        bool active,
        BankType type,
        int bankBranchId)
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext($"{nameof(Creating_bank_loan_rejects_unavailable_or_foreign_banks)}-{active}-{type}-{bankBranchId}");
        var (_, bank, _) = await SeedBankContext(db, now);
        bank.Active = active;
        bank.Type = type;
        bank.BranchId = bankBranchId;
        await db.SaveChangesAsync();

        var handler = new CreateBranchInformalLoanHandler(db, new CurrentUser());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateBranchInformalLoanCommand
            {
                BranchId = 1,
                Dto = new CreateBranchInformalLoanDto
                {
                    BankId = bank.Id,
                    Concept = "Préstamo bancario",
                    Amount = 300000m
                }
            },
            CancellationToken.None));
    }
}
