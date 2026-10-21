using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>
/// Synthetic business integration database. No API host, real secrets or remote services.
/// A supplied server must be loopback; its database is never initialized or modified.
/// This fixture validates PostgreSQL transactions and EF tenant filters, not production RLS.
/// </summary>
internal sealed class RappiIsolatedBusinessDatabase : IAsyncDisposable
{
    private PostgreSqlContainer? postgres;
    private string? serverConnection;
    private string? databaseName;
    private DbContextOptions<ApplicationDbContext> options = null!;

    internal ApplicationDbContext Db(int tenantId = 1, IInterceptor? interceptor = null)
        => Db(new TestTenantContext(tenantId), interceptor);

    internal ApplicationDbContext Db(TestTenantContext tenant, IInterceptor? interceptor = null)
    {
        var configured = interceptor is null ? options
            : new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(interceptor).Options;
        return new(configured, currentTenant: tenant, tenantExecutionContext: tenant);
    }

    internal static async Task<RappiIsolatedBusinessDatabase> CreateAsync()
    {
        var fixture = new RappiIsolatedBusinessDatabase();
        try
        {
            var supplied = Environment.GetEnvironmentVariable("RAPPI_POSTGRES_TEST_CONNECTION");
            string connection;
            if (!string.IsNullOrWhiteSpace(supplied))
            {
                var builder = new NpgsqlConnectionStringBuilder(supplied);
                if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
                    throw new InvalidOperationException("Rappi business tests require a loopback PostgreSQL server.");
                builder.Database = "postgres";
                builder.Pooling = false;
                fixture.serverConnection = builder.ConnectionString;
                fixture.databaseName = $"rappi_business_test_{Guid.NewGuid():N}";
                await using var admin = new NpgsqlConnection(fixture.serverConnection);
                await admin.OpenAsync();
                await new NpgsqlCommand($"CREATE DATABASE {fixture.databaseName}", admin).ExecuteNonQueryAsync();
                builder.Database = fixture.databaseName;
                connection = builder.ConnectionString;
            }
            else
            {
                fixture.postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await fixture.postgres.StartAsync();
                connection = fixture.postgres.GetConnectionString();
            }
            fixture.options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
            await using var db = fixture.Db();
            await db.Database.EnsureCreatedAsync();
            db.Tenants.AddRange(new Tenant { Id = 1, Name = "Synthetic A", Slug = "rappi-test-a" },
                new Tenant { Id = 2, Name = "Synthetic B", Slug = "rappi-test-b" });
            await db.SaveChangesAsync();
            await fixture.SeedBranchAsync(1, 11, BranchPrintSettings.KitchenAutoPrintWhenOrderCreated);
            await fixture.SeedBranchAsync(1, 12, BranchPrintSettings.KitchenAutoPrintWhenMarkedReady);
            await fixture.SeedBranchAsync(2, 21, BranchPrintSettings.KitchenAutoPrintWhenOrderCreated);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task SeedBranchAsync(int tenantId, int id, string printTrigger)
    {
        await using var db = Db(tenantId);
        var branch = new Branch { Id = id, Name = $"Synthetic branch {id}", Address = "Local only", Phone1 = "000" };
        var user = new User { Id = id, Branch = branch, BranchId = id, Name = "Synthetic Rappi", Email = $"rappi-{id}@example.invalid", Phone = "000", PasswordHash = "unused", Role = UserRole.Admin };
        var customer = new Customer { Id = id, Branch = branch, BranchId = id, Name = "Synthetic customer" };
        var bank = new Bank { Id = id, Branch = branch, BranchId = id, Name = "Synthetic bank" };
        var app = new App { Id = id, Bank = bank, BankId = id, Name = "Synthetic Rappi" };
        var category = new ProductCategory { Id = id, Branch = branch, BranchId = id, Name = "Synthetic drinks" };
        var product = new Product { Id = id, Category = category, CategoryId = id, Name = "Synthetic strict drink", Price = 18000, Active = true, InventoryEnabled = true, InventoryControlMode = InventoryControlMode.Strict };
        var expenseCategory = new ExpenseCategory { Id = id, Name = $"Synthetic supplies {id}" };
        var expense = new Expense { Id = id, Category = expenseCategory, CategoryId = id, Name = "Synthetic unit", TracksInventory = true, InventoryActive = true, InventoryBaseUnit = InventoryBaseUnit.Unit };
        var connection = new DeliveryAppConnection
        {
            Id = id, Branch = branch, BranchId = id, Provider = "rappi", Environment = "isolated-test",
            IsActive = true, IsVerified = true, FinancialApp = app, FinancialAppId = id,
            TechnicalUser = user, TechnicalUserId = id, Customer = customer, CustomerId = id,
            DefaultCookingTimeMinutes = 25, EstimatedCommissionRate = .25m
        };
        connection.Stores.Add(new DeliveryAppStore { Id = id, RappiStoreId = $"isolated-store-{id}", StoreIntegrationId = $"isolated-store-{id}", Name = "Synthetic store", IsParent = true, ManualReadyForPickupEnabled = true });
        connection.ProductMappings.Add(new DeliveryAppProductMapping { Id = id, Product = product, ProductId = id, Sku = $"product-{id}", CategorySku = $"category-{id}", IsSelected = true, PublishedPrice = 18000, PublishedAt = DateTime.UtcNow });
        db.DeliveryAppConnections.Add(connection);
        db.Expenses.Add(expense);
        db.ProductExpenseRequirements.Add(new ProductExpenseRequirement { Product = product, ProductId = id, Expense = expense, ExpenseId = id, BaseQuantity = 1 });
        db.InventoryBalances.Add(new InventoryBalance { Branch = branch, BranchId = id, Expense = expense, ExpenseId = id, QuantityOnHand = 5, AverageUnitCost = 1000 });
        db.BranchPrintSettings.Add(new BranchPrintSettings { Branch = branch, BranchId = id, EnableKitchenJobs = true, KitchenAutoPrintTrigger = printTrigger });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (serverConnection is not null && databaseName is not null)
        {
            await using var admin = new NpgsqlConnection(serverConnection);
            await admin.OpenAsync();
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin).ExecuteNonQueryAsync();
        }
        if (postgres is not null) await postgres.DisposeAsync();
    }
}
