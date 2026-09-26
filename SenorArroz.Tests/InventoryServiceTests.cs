using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Application.Features.Inventory.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class InventoryServiceTests
{
    private sealed class CurrentUser(string role = "Admin") : ICurrentUser
    {
        public int Id => 99;
        public string Role => role;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Context(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);

    private static InventoryService Service(ApplicationDbContext db, string role = "Admin") => new(
        db,
        new CurrentUser(role),
        TestTenantContext.Default,
        new SystemUtcClock(),
        new TestBranchContext());

    [Fact]
    public async Task EstimatedConsumption_CanLeaveTheoreticalBalanceNegative()
    {
        await using var db = Context(nameof(EstimatedConsumption_CanLeaveTheoreticalBalanceNegative));
        var category = new ProductCategory { Id = 1, BranchId = 1, Name = "Arroces" };
        var expense = new Expense { Id = 1, Name = "Arroz", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Gram };
        var product = new Product { Id = 1, CategoryId = 1, Category = category, Name = "Arroz personal", Active = true, InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Estimated };
        var order = new Order { Id = 1, BranchId = 1, Type = OrderType.Onsite, OrderDetails = [new OrderDetail { Id = 1, ProductId = 1, Product = product, Quantity = 2 }] };
        db.AddRange(category, expense, product, new ProductExpenseRequirement { ProductId = 1, ExpenseId = 1, BaseQuantity = 100 }, order);
        await db.SaveChangesAsync();

        var service = Service(db);
        await service.SnapshotAndReserveAsync(order, false, "estimated:snapshot", default);
        await service.ConsumeOrderAsync(order, "estimated:consume", default);

        var balance = await db.InventoryBalances.SingleAsync();
        Assert.Equal(-200, balance.QuantityOnHand);
        Assert.Equal(0, balance.QuantityReserved);
        Assert.Contains(db.InventoryMovements, x => x.Type == InventoryMovementType.EstimatedConsumption && x.OnHandDelta == -200);
    }

    [Fact]
    public async Task StrictReservation_BlocksASecondOrderForTheLastUnit()
    {
        await using var db = Context(nameof(StrictReservation_BlocksASecondOrderForTheLastUnit));
        var category = new ProductCategory { Id = 1, BranchId = 1, Name = "Bebidas" };
        var expense = new Expense { Id = 1, Name = "Gaseosa", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Unit };
        var product = new Product { Id = 1, CategoryId = 1, Category = category, Name = "Gaseosa", Active = true, InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict };
        var first = new Order { Id = 1, BranchId = 1, Type = OrderType.Onsite, OrderDetails = [new OrderDetail { Id = 1, ProductId = 1, Product = product, Quantity = 1 }] };
        var second = new Order { Id = 2, BranchId = 1, Type = OrderType.Onsite, OrderDetails = [new OrderDetail { Id = 2, ProductId = 1, Product = product, Quantity = 1 }] };
        db.AddRange(category, expense, product, new ProductExpenseRequirement { ProductId = 1, ExpenseId = 1, BaseQuantity = 1 }, new InventoryBalance { BranchId = 1, ExpenseId = 1, QuantityOnHand = 1 }, first, second);
        await db.SaveChangesAsync();

        var service = Service(db);
        await service.SnapshotAndReserveAsync(first, false, "strict:first", default);
        await Assert.ThrowsAsync<BusinessException>(() => service.SnapshotAndReserveAsync(second, false, "strict:second", default));

        var balance = await db.InventoryBalances.SingleAsync();
        Assert.Equal(1, balance.QuantityReserved);
        Assert.Single(await db.InventoryMovements.Where(x => x.Type == InventoryMovementType.Reservation).ToListAsync());
    }

    [Fact]
    public async Task RecordingPurchase_IncreasesBalanceAndCalculatesWeightedCostOnce()
    {
        await using var db = Context(nameof(RecordingPurchase_IncreasesBalanceAndCalculatesWeightedCostOnce));
        var expense = new Expense { Id = 1, Name = "Gaseosa", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Unit };
        var conversion = new ExpenseUnitConversion { Id = 1, ExpenseId = 1, Name = "Caja x24", BaseQuantity = 24, Active = true };
        var header = new ExpenseHeader
        {
            Id = 1,
            BranchId = 1,
            ExpenseDetails = [new ExpenseDetail { Id = 1, ExpenseId = 1, Quantity = 2, Total = 48000, InventoryConversionId = 1 }]
        };
        db.AddRange(expense, conversion, new InventoryBalance { BranchId = 1, ExpenseId = 1, QuantityOnHand = 24, AverageUnitCost = 900 }, header);
        await db.SaveChangesAsync();

        var service = Service(db);
        await service.RecordPurchaseAsync(header, "purchase:1", default);
        await service.RecordPurchaseAsync(header, "purchase:1", default);

        var balance = await db.InventoryBalances.SingleAsync();
        Assert.Equal(72, balance.QuantityOnHand);
        Assert.Equal(966.666667m, decimal.Round(balance.AverageUnitCost, 6));
        var movement = Assert.Single(await db.InventoryMovements.Where(x => x.Type == InventoryMovementType.Purchase).ToListAsync());
        Assert.Equal(header.Id, movement.ExpenseHeaderId);
    }

    [Fact]
    public async Task CreatingTransfer_WithSameOperationKey_ReturnsExistingDraft()
    {
        await using var db = Context(nameof(CreatingTransfer_WithSameOperationKey_ReturnsExistingDraft));
        db.AddRange(
            new Branch { Id = 1, Name = "Origen", Address = "Origen", Phone1 = "1" },
            new Branch { Id = 2, Name = "Destino", Address = "Destino", Phone1 = "2" },
            new Expense { Id = 1, Name = "Gaseosa", TracksInventory = true, InventoryActive = true });
        await db.SaveChangesAsync();

        var service = Service(db);
        var input = new SenorArroz.Application.Features.Inventory.DTOs.InventoryTransferInput(1, 2,
            [new SenorArroz.Application.Features.Inventory.DTOs.InventoryTransferLineInput(1, 4)]);

        var first = await service.CreateTransferAsync(input, "transfer:create:1", default);
        var retry = await service.CreateTransferAsync(input, "transfer:create:1", default);

        Assert.Equal(first.Id, retry.Id);
        Assert.Single(await db.InventoryTransfers.ToListAsync());
    }

    [Fact]
    public async Task ReversingPurchase_RemovesItsValueFromAverageCost()
    {
        await using var db = Context(nameof(ReversingPurchase_RemovesItsValueFromAverageCost));
        var expense = new Expense { Id = 1, Name = "Carne", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Gram };
        var header = new ExpenseHeader
        {
            Id = 1,
            BranchId = 1,
            ExpenseDetails = [new ExpenseDetail { Id = 1, ExpenseId = 1, InventoryBaseQuantity = 10, InventoryUnitCost = 200 }]
        };
        db.AddRange(expense, header, new InventoryBalance { BranchId = 1, ExpenseId = 1, QuantityOnHand = 20, AverageUnitCost = 150 });
        await db.SaveChangesAsync();

        await Service(db).ReversePurchaseAsync(header, "purchase:reverse:1", default);

        var balance = await db.InventoryBalances.SingleAsync();
        Assert.Equal(10, balance.QuantityOnHand);
        Assert.Equal(100, balance.AverageUnitCost);
    }

    [Fact]
    public async Task StrictReservation_DoesNotPartiallyReserveWhenAnotherIngredientIsMissing()
    {
        await using var db = Context(nameof(StrictReservation_DoesNotPartiallyReserveWhenAnotherIngredientIsMissing));
        var category = new ProductCategory { Id = 1, BranchId = 1, Name = "Bebidas" };
        var firstExpense = new Expense { Id = 1, Name = "Botella", TracksInventory = true, InventoryActive = true };
        var secondExpense = new Expense { Id = 2, Name = "Tapa", TracksInventory = true, InventoryActive = true };
        var product = new Product { Id = 1, CategoryId = 1, Category = category, Name = "Bebida", Active = true, InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict };
        var order = new Order { Id = 1, BranchId = 1, Type = OrderType.Onsite, OrderDetails = [new OrderDetail { Id = 1, ProductId = 1, Product = product, Quantity = 1 }] };
        db.AddRange(
            category,
            firstExpense,
            secondExpense,
            product,
            new ProductExpenseRequirement { ProductId = 1, ExpenseId = 1, BaseQuantity = 1 },
            new ProductExpenseRequirement { ProductId = 1, ExpenseId = 2, BaseQuantity = 1 },
            new InventoryBalance { BranchId = 1, ExpenseId = 1, QuantityOnHand = 10 },
            new InventoryBalance { BranchId = 1, ExpenseId = 2, QuantityOnHand = 0 },
            order);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => Service(db).SnapshotAndReserveAsync(order, false, "strict:atomic", default));

        Assert.All(await db.InventoryBalances.ToListAsync(), balance => Assert.Equal(0, balance.QuantityReserved));
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Availability_DoesNotExposeAProductFromAnotherBranch()
    {
        await using var db = Context(nameof(Availability_DoesNotExposeAProductFromAnotherBranch));
        var category = new ProductCategory { Id = 2, BranchId = 2, Name = "Bebidas" };
        var product = new Product { Id = 2, CategoryId = 2, Category = category, Name = "Bebida externa", Active = true };
        db.AddRange(new Branch { Id = 2, Name = "Otra", Address = "-", Phone1 = "2" }, category, product);
        await db.SaveChangesAsync();

        var result = await Service(db).GetAvailabilityAsync([product.Id], 1, default);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReplacingConversions_DeactivatesAUsedPresentationInsteadOfDeletingIt()
    {
        await using var db = Context(nameof(ReplacingConversions_DeactivatesAUsedPresentationInsteadOfDeletingIt));
        var expense = new Expense { Id = 1, Name = "Arroz", TracksInventory = true, InventoryActive = true };
        var conversion = new ExpenseUnitConversion { Id = 1, ExpenseId = 1, Name = "Bulto", BaseQuantity = 25000, Active = true };
        db.AddRange(expense, conversion);
        await db.SaveChangesAsync();

        SenorArroz.Application.Features.Inventory.Helpers.InventoryCatalogHelper.ReplaceConversions(1, [], db);
        await db.SaveChangesAsync();

        Assert.False((await db.ExpenseUnitConversions.SingleAsync()).Active);
    }

    [Fact]
    public async Task CopyingCatalogConfiguration_UpdatesOnlyInventoryFieldsAndDeactivatesOldConversions()
    {
        await using var db = Context(nameof(CopyingCatalogConfiguration_UpdatesOnlyInventoryFieldsAndDeactivatesOldConversions));
        var source = new Expense
        {
            Id = 1,
            Name = "Coca Cola 1.5 L",
            CategoryId = 10,
            Unit = ExpenseUnit.Package,
            TracksInventory = true,
            InventoryActive = true,
            InventoryBaseUnit = InventoryBaseUnit.Unit
        };
        var target = new Expense
        {
            Id = 2,
            Name = "Coca Cola personal",
            CategoryId = 20,
            Unit = ExpenseUnit.Unit,
            TracksInventory = false,
            InventoryActive = false,
            InventoryBaseUnit = InventoryBaseUnit.Milliliter
        };
        db.AddRange(
            source,
            target,
            new ExpenseUnitConversion { ExpenseId = 1, Name = "Caja x12", BaseQuantity = 12, Active = true },
            new ExpenseUnitConversion { ExpenseId = 2, Name = "Canasta", BaseQuantity = 24, Active = true });
        await db.SaveChangesAsync();

        var result = await Service(db).CopyCatalogConfigurationAsync(1, [2], default);

        db.ChangeTracker.Clear();
        var updated = await db.Expenses.SingleAsync(x => x.Id == 2);
        var conversions = await db.ExpenseUnitConversions.Where(x => x.ExpenseId == 2).OrderBy(x => x.Name).ToListAsync();
        Assert.Equal([2], result.UpdatedExpenseIds);
        Assert.Equal("Coca Cola personal", updated.Name);
        Assert.Equal(20, updated.CategoryId);
        Assert.Equal(ExpenseUnit.Unit, updated.Unit);
        Assert.True(updated.TracksInventory);
        Assert.True(updated.InventoryActive);
        Assert.Equal(InventoryBaseUnit.Unit, updated.InventoryBaseUnit);
        Assert.Contains(conversions, x => x.Name == "Caja x12" && x.BaseQuantity == 12 && x.Active);
        Assert.Contains(conversions, x => x.Name == "Canasta" && !x.Active);
    }

    [Fact]
    public async Task CopyingCatalogConfiguration_WithInvalidDestination_DoesNotModifyValidDestinations()
    {
        await using var db = Context(nameof(CopyingCatalogConfiguration_WithInvalidDestination_DoesNotModifyValidDestinations));
        db.AddRange(
            new Expense { Id = 1, Name = "Origen", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Unit },
            new Expense { Id = 2, Name = "Destino", TracksInventory = false, InventoryActive = false, InventoryBaseUnit = InventoryBaseUnit.Gram },
            new ExpenseUnitConversion { ExpenseId = 1, Name = "Caja x12", BaseQuantity = 12, Active = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => Service(db).CopyCatalogConfigurationAsync(1, [2, 999], default));

        db.ChangeTracker.Clear();
        var target = await db.Expenses.SingleAsync(x => x.Id == 2);
        Assert.False(target.TracksInventory);
        Assert.False(target.InventoryActive);
        Assert.Equal(InventoryBaseUnit.Gram, target.InventoryBaseUnit);
        Assert.Empty(await db.ExpenseUnitConversions.Where(x => x.ExpenseId == 2).ToListAsync());
    }

    [Fact]
    public async Task CopyingCatalogConfiguration_RejectsNonAdministrativeUsers()
    {
        await using var db = Context(nameof(CopyingCatalogConfiguration_RejectsNonAdministrativeUsers));
        db.AddRange(
            new Expense { Id = 1, Name = "Origen", TracksInventory = true, InventoryActive = true },
            new Expense { Id = 2, Name = "Destino" },
            new ExpenseUnitConversion { ExpenseId = 1, Name = "Unidad", BaseQuantity = 1, Active = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => Service(db, "Cashier").CopyCatalogConfigurationAsync(1, [2], default));
    }

    [Fact]
    public async Task CopyingCatalogConfiguration_CannotTargetAnotherTenant()
    {
        await using var db = Context(nameof(CopyingCatalogConfiguration_CannotTargetAnotherTenant));
        db.AddRange(
            new Expense { Id = 1, Name = "Origen", TracksInventory = true, InventoryActive = true },
            new ExpenseUnitConversion { ExpenseId = 1, Name = "Unidad", BaseQuantity = 1, Active = true });
        await db.SaveChangesAsync();
        using (TestTenantContext.Default.BeginSystemScope())
        {
            db.Expenses.Add(new Expense { Id = 2, TenantId = 2, Name = "Destino ajeno", InventoryBaseUnit = InventoryBaseUnit.Gram });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BusinessException>(() => Service(db).CopyCatalogConfigurationAsync(1, [2], default));

        using (TestTenantContext.Default.BeginSystemScope())
        {
            var foreignTarget = await db.Expenses.SingleAsync(x => x.Id == 2);
            Assert.False(foreignTarget.TracksInventory);
            Assert.False(foreignTarget.InventoryActive);
            Assert.Equal(InventoryBaseUnit.Gram, foreignTarget.InventoryBaseUnit);
        }
    }

    [Fact]
    public async Task SavingCountDraft_UpdatesOnlySubmittedLinesAndKeepsCountOpen()
    {
        await using var db = Context(nameof(SavingCountDraft_UpdatesOnlySubmittedLinesAndKeepsCountOpen));
        db.AddRange(
            new Expense { Id = 1, Name = "Arroz", TracksInventory = true, InventoryActive = true },
            new Expense { Id = 2, Name = "Aceite", TracksInventory = true, InventoryActive = true });
        var count = new InventoryCount
        {
            Id = 1,
            BranchId = 1,
            CreatedById = 99,
            Lines =
            [
                new InventoryCountLine { ExpenseId = 1, ExpectedQuantity = 10 },
                new InventoryCountLine { ExpenseId = 2, ExpectedQuantity = 5 }
            ]
        };
        db.InventoryCounts.Add(count);
        await db.SaveChangesAsync();

        var result = await Service(db).SaveCountDraftAsync(1, [new InventoryCountDraftLineInput(1, 8)], default);

        Assert.Equal(InventoryCountStatus.Draft, result.Status);
        Assert.Equal(8, result.Lines.Single(x => x.ExpenseId == 1).CountedQuantity);
        Assert.Null(result.Lines.Single(x => x.ExpenseId == 2).CountedQuantity);
        Assert.All(result.Lines, x => Assert.Null(x.Difference));
    }

    [Fact]
    public async Task SavingCountDraft_RejectsLinesOutsideTheCount()
    {
        await using var db = Context(nameof(SavingCountDraft_RejectsLinesOutsideTheCount));
        db.AddRange(
            new Expense { Id = 1, Name = "Arroz", TracksInventory = true, InventoryActive = true },
            new InventoryCount
            {
                Id = 1,
                BranchId = 1,
                CreatedById = 99,
                Lines = [new InventoryCountLine { ExpenseId = 1, ExpectedQuantity = 10 }]
            });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => Service(db).SaveCountDraftAsync(1, [new InventoryCountDraftLineInput(999, 2)], default));
    }

    [Fact]
    public async Task SavingCountDraft_RejectsAnotherBranch()
    {
        await using var db = Context(nameof(SavingCountDraft_RejectsAnotherBranch));
        db.AddRange(
            new Expense { Id = 1, Name = "Arroz", TracksInventory = true, InventoryActive = true },
            new InventoryCount
            {
                Id = 1,
                BranchId = 2,
                CreatedById = 99,
                Lines = [new InventoryCountLine { ExpenseId = 1, ExpectedQuantity = 10 }]
            });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BranchScopeMismatchException>(() => Service(db).SaveCountDraftAsync(1, [new InventoryCountDraftLineInput(1, 8)], default));
    }
}
