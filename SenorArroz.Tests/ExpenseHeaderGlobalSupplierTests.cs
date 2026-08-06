using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.Commands;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class ExpenseHeaderGlobalSupplierTests
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

    private static CreateExpenseHeaderHandler BuildCreateHandler(ApplicationDbContext db, DateTime now) =>
        new(
            new ExpenseHeaderRepository(db),
            new BankRepository(db),
            db,
            CreateMapper(),
            new TestCurrentUser(),
            new TestBranchContext(1),
            new FakeClock(now));

    private static UpdateExpenseHeaderHandler BuildUpdateHandler(ApplicationDbContext db, DateTime now) =>
        new(
            new ExpenseHeaderRepository(db),
            new BankRepository(db),
            db,
            CreateMapper(),
            new TestCurrentUser(),
            new TestBranchContext(1),
            new FakeClock(now));

    private static async Task<SeededExpenseData> SeedBaseAsync(ApplicationDbContext db, DateTime now, bool includeHeader = false)
    {
        var branch1 = new Branch
        {
            Id = 1,
            Name = "Santander",
            Address = "A",
            Phone1 = "3000000001",
            CreatedAt = now,
            UpdatedAt = now
        };
        var branch2 = new Branch
        {
            Id = 2,
            Name = "Manrique",
            Address = "B",
            Phone1 = "3000000002",
            CreatedAt = now,
            UpdatedAt = now
        };
        var cashier = new User
        {
            Id = 1,
            BranchId = branch1.Id,
            Role = UserRole.Cashier,
            Name = "Caja",
            Email = "caja@test.com",
            Phone = "3000000003",
            PasswordHash = "x",
            Branch = branch1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var otherDeliveryman = new User
        {
            Id = 2,
            BranchId = branch2.Id,
            Role = UserRole.Deliveryman,
            Name = "Domi Manrique",
            Email = "domi@test.com",
            Phone = "3000000004",
            PasswordHash = "x",
            Branch = branch2,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var supplier1 = new Supplier
        {
            Id = 1,
            BranchId = branch1.Id,
            Name = "Proveedor Santander",
            Phone = "3000000005",
            CreatedAt = now,
            UpdatedAt = now
        };
        var supplier2 = new Supplier
        {
            Id = 2,
            BranchId = branch2.Id,
            Name = "Proveedor Manrique",
            Phone = "3000000006",
            CreatedAt = now,
            UpdatedAt = now
        };
        var category = new ExpenseCategory
        {
            Id = 1,
            Name = "Categoria",
            CreatedAt = now,
            UpdatedAt = now
        };
        var expense = new Expense
        {
            Id = 1,
            Name = "Insumo",
            CategoryId = category.Id,
            Category = category,
            Unit = ExpenseUnit.Unit,
            CreatedAt = now,
            UpdatedAt = now
        };
        var localBank = new Bank
        {
            Id = 1,
            BranchId = branch1.Id,
            Name = "Banco Santander",
            Active = true,
            Branch = branch1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var otherBank = new Bank
        {
            Id = 2,
            BranchId = branch2.Id,
            Name = "Banco Manrique",
            Active = true,
            Branch = branch2,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AddRange(branch1, branch2, cashier, otherDeliveryman, supplier1, supplier2, category, expense, localBank, otherBank);

        ExpenseHeader? header = null;
        if (includeHeader)
        {
            header = new ExpenseHeader
            {
                Id = 1,
                BranchId = branch1.Id,
                SupplierId = supplier1.Id,
                CreatedById = cashier.Id,
                Total = 1000m,
                Branch = branch1,
                Supplier = supplier1,
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
                        UpdatedAt = now
                    }
                }
            };
            db.ExpenseHeaders.Add(header);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new SeededExpenseData(supplier1.Id, supplier2.Id, expense.Id, localBank.Id, otherBank.Id, otherDeliveryman.Id, header?.Id ?? 0);
    }

    [Fact]
    public async Task Create_expense_header_allows_supplier_from_other_origin_branch()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Create_expense_header_allows_supplier_from_other_origin_branch));
        var seeded = await SeedBaseAsync(db, now);

        var result = await BuildCreateHandler(db, now).Handle(new CreateExpenseHeaderCommand
        {
            ExpenseHeader = new CreateExpenseHeaderDto
            {
                SupplierId = seeded.OtherSupplierId,
                IncludeVat = false,
                ExpenseBankPayments = [],
                ExpenseDetails =
                [
                    new CreateExpenseDetailDto
                    {
                        ExpenseId = seeded.ExpenseId,
                        Quantity = 1,
                        Amount = 1000,
                        Total = 1000m
                    }
                ]
            }
        }, CancellationToken.None);

        Assert.Equal(1, result.BranchId);
        Assert.Equal(seeded.OtherSupplierId, result.SupplierId);
        Assert.Equal("Proveedor Manrique", result.SupplierName);
    }

    [Fact]
    public async Task Update_expense_header_allows_switching_to_supplier_from_other_origin_branch()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Update_expense_header_allows_switching_to_supplier_from_other_origin_branch));
        var seeded = await SeedBaseAsync(db, now, includeHeader: true);

        var result = await BuildUpdateHandler(db, now).Handle(new UpdateExpenseHeaderCommand
        {
            Id = seeded.HeaderId,
            ExpenseHeader = new UpdateExpenseHeaderDto
            {
                SupplierId = seeded.OtherSupplierId,
                IncludeVat = false,
                ExpenseBankPayments = [],
                ExpenseDetails =
                [
                    new UpdateExpenseDetailDto
                    {
                        Id = 1,
                        ExpenseId = seeded.ExpenseId,
                        Quantity = 1,
                        Amount = 1000,
                        Total = 1000m
                    }
                ]
            }
        }, CancellationToken.None);

        Assert.Equal(1, result.BranchId);
        Assert.Equal(seeded.OtherSupplierId, result.SupplierId);
        Assert.Equal("Proveedor Manrique", result.SupplierName);
    }

    [Fact]
    public async Task Create_expense_header_still_rejects_bank_from_other_branch()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Create_expense_header_still_rejects_bank_from_other_branch));
        var seeded = await SeedBaseAsync(db, now);

        await Assert.ThrowsAsync<NotFoundException>(() => BuildCreateHandler(db, now).Handle(new CreateExpenseHeaderCommand
        {
            ExpenseHeader = new CreateExpenseHeaderDto
            {
                SupplierId = seeded.OtherSupplierId,
                IncludeVat = false,
                ExpenseBankPayments = [new CreateExpenseBankPaymentDto { BankId = seeded.OtherBankId, Amount = 1000m }],
                ExpenseDetails =
                [
                    new CreateExpenseDetailDto
                    {
                        ExpenseId = seeded.ExpenseId,
                        Quantity = 1,
                        Amount = 1000,
                        Total = 1000m
                    }
                ]
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Create_expense_header_still_rejects_deliveryman_from_other_branch()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        using var db = CreateCtx(nameof(Create_expense_header_still_rejects_deliveryman_from_other_branch));
        var seeded = await SeedBaseAsync(db, now);

        await Assert.ThrowsAsync<BusinessException>(() => BuildCreateHandler(db, now).Handle(new CreateExpenseHeaderCommand
        {
            ExpenseHeader = new CreateExpenseHeaderDto
            {
                SupplierId = seeded.OtherSupplierId,
                DeliverymanId = seeded.OtherDeliverymanId,
                IncludeVat = false,
                ExpenseBankPayments = [],
                ExpenseDetails =
                [
                    new CreateExpenseDetailDto
                    {
                        ExpenseId = seeded.ExpenseId,
                        Quantity = 1,
                        Amount = 1000,
                        Total = 1000m
                    }
                ]
            }
        }, CancellationToken.None));
    }

    private sealed record SeededExpenseData(
        int LocalSupplierId,
        int OtherSupplierId,
        int ExpenseId,
        int LocalBankId,
        int OtherBankId,
        int OtherDeliverymanId,
        int HeaderId);
}
