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
}
