using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Inventory.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class InventoryServiceTests
{
    private sealed class CurrentUser : ICurrentUser
    {
        public int Id => 99;
        public string Role => "Admin";
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Context(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);

    private static InventoryService Service(ApplicationDbContext db) => new(
        db,
        new CurrentUser(),
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
}
