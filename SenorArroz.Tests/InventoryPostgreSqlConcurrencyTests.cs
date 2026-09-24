using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Inventory.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class InventoryPostgreSqlConcurrencyTests : IAsyncLifetime
{
    private sealed class CurrentUser : ICurrentUser
    {
        public int Id => 1;
        public string Role => "Admin";
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<ApplicationDbContext> options = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = 1, Name = "Tenant", Slug = "tenant" });
        db.Branches.Add(new Branch { Id = 1, Name = "Centro", Address = "-", Phone1 = "3000000000" });
        db.Users.Add(new User { Id = 1, BranchId = 1, Name = "Admin", Email = "admin@test.local", Phone = "3000000001", PasswordHash = "x" });
        db.ProductCategories.Add(new ProductCategory { Id = 1, BranchId = 1, Name = "Bebidas" });
        db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Compras" });
        db.Expenses.Add(new Expense { Id = 1, CategoryId = 1, Name = "Gaseosa", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Unit });
        db.Products.Add(new Product { Id = 1, CategoryId = 1, Name = "Gaseosa", Price = 5000, Active = true, InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict });
        db.ProductExpenseRequirements.Add(new ProductExpenseRequirement { ProductId = 1, ExpenseId = 1, BaseQuantity = 1 });
        db.InventoryBalances.Add(new InventoryBalance { BranchId = 1, ExpenseId = 1, QuantityOnHand = 1 });
        db.Orders.AddRange(
            new Order { Id = 1, BranchId = 1, TakenById = 1, Type = OrderType.Onsite, OrderDetails = [new OrderDetail { ProductId = 1, Quantity = 1, UnitPrice = 5000 }] },
            new Order { Id = 2, BranchId = 1, TakenById = 1, Type = OrderType.Onsite, OrderDetails = [new OrderDetail { ProductId = 1, Quantity = 1, UnitPrice = 5000 }] });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Concurrent_orders_cannot_reserve_the_same_last_unit()
    {
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var firstOrder = await firstDb.Orders.Include(x => x.OrderDetails).SingleAsync(x => x.Id == 1);
        var secondOrder = await secondDb.Orders.Include(x => x.OrderDetails).SingleAsync(x => x.Id == 2);
        var firstService = Service(firstDb);
        var secondService = Service(secondDb);

        var results = await Task.WhenAll(
            Capture(() => firstService.SnapshotAndReserveAsync(firstOrder, false, "concurrent:first", default)),
            Capture(() => secondService.SnapshotAndReserveAsync(secondOrder, false, "concurrent:second", default)));

        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x is not null);
        await using var verification = CreateDb();
        Assert.Equal(1, (await verification.InventoryBalances.SingleAsync()).QuantityReserved);
        Assert.Single(await verification.InventoryMovements.Where(x => x.Type == InventoryMovementType.Reservation).ToListAsync());
    }

    private ApplicationDbContext CreateDb() => new(options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);

    private static InventoryService Service(ApplicationDbContext db) => new(db, new CurrentUser(), TestTenantContext.Default, new SystemUtcClock(), new TestBranchContext());

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception exception) { return exception; }
    }
}
