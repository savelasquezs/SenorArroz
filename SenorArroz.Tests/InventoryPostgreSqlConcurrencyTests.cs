using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Inventory.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
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
        await db.Database.ExecuteSqlRawAsync(ReadScript("enable_multitenant_rls_v3.sql"));
        await db.Database.ExecuteSqlRawAsync(ReadScript("add_inventory_core.sql"));
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

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Inventory_schema_script_creates_four_RLS_policies_per_table()
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*)::integer
            FROM pg_policies
            WHERE schemaname = 'public'
              AND tablename = ANY (ARRAY[
                'expense_unit_conversion','product_expense_requirement','inventory_balance',
                'inventory_movement','order_inventory_allocation','inventory_transfer',
                'inventory_transfer_line','inventory_count','inventory_count_line'])
              AND policyname IN ('tenant_select_policy','tenant_insert_policy','tenant_update_policy','tenant_delete_policy')
            """, connection);

        Assert.Equal(36, (int)(await command.ExecuteScalarAsync())!);
    }

    // Regression coverage for the inventory merge blockers: branch-scoped idempotency and historical purchase origins.
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Expense_header_inventory_idempotency_index_is_scoped_by_branch()
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = 'ux_expense_header_inventory_operation'
            """, connection);

        var definition = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("(tenant_id, branch_id, inventory_operation_key)", definition, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Inventory_movement_keeps_expense_header_id_without_delete_blocking_foreign_key()
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*)::integer
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(c.conkey)
            WHERE c.contype = 'f'
              AND t.relname = 'inventory_movement'
              AND a.attname = 'expense_header_id'
            """, connection);

        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Order_repository_update_reuses_an_existing_transaction()
    {
        await using var db = CreateDb();
        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.OrderDetails)
            .SingleAsync(x => x.Id == 1);
        order.Notes = "edited inside inventory transaction";

        await using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new OrderRepository(db, new SystemUtcClock(), TestTenantContext.Default);

        await repository.UpdateAsync(order);
        await transaction.CommitAsync();

        db.ChangeTracker.Clear();
        Assert.Equal(
            "edited inside inventory transaction",
            (await db.Orders.AsNoTracking().SingleAsync(x => x.Id == 1)).Notes);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Inventory_balance_constraints_reject_invalid_reservations()
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var scope = new NpgsqlCommand("SET app.current_tenant_id = '1'", connection))
            await scope.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE inventory_balance
            SET quantity_reserved = quantity_on_hand + 1
            WHERE tenant_id = 1 AND branch_id = 1 AND expense_id = 1
            """, connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Inventory_RLS_hides_rows_from_another_tenant_for_a_non_privileged_role()
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("""
            CREATE ROLE inventory_runtime_test NOLOGIN NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public, app TO inventory_runtime_test;
            GRANT SELECT ON inventory_balance TO inventory_runtime_test;
            """, connection))
            await setup.ExecuteNonQueryAsync();

        await using var command = new NpgsqlCommand("""
            SET ROLE inventory_runtime_test;
            WITH scope AS MATERIALIZED (
                SELECT set_config('app.current_tenant_id', '2', false)
            )
            SELECT count(*)::integer FROM inventory_balance CROSS JOIN scope;
            """, connection);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }

    private ApplicationDbContext CreateDb() => new(options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);

    private static InventoryService Service(ApplicationDbContext db) => new(db, new CurrentUser(), TestTenantContext.Default, new SystemUtcClock(), new TestBranchContext());

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception exception) { return exception; }
    }

    private static string ReadScript(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SenorArroz.Infrastructure", "Scripts", name);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        throw new FileNotFoundException($"No se encontrÃ³ el script {name}.");
    }
}
