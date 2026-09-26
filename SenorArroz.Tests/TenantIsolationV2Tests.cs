using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class TenantIsolationV2Tests
{
    [Fact]
    public void Model_has_one_global_entity_and_complete_tenant_contracts()
    {
        using var db = CreateDb(new TestTenantContext());
        var entityTypes = db.Model.GetEntityTypes()
            .Where(x => !x.IsOwned() && x.ClrType is not null)
            .ToList();
        var tenantOwned = entityTypes
            .Where(x => typeof(ITenantOwned).IsAssignableFrom(x.ClrType))
            .ToList();
        var global = entityTypes.Except(tenantOwned).ToList();

        Assert.Equal(104, entityTypes.Count);
        Assert.Equal(103, tenantOwned.Count);
        Assert.Single(global);
        Assert.Equal(typeof(Tenant), global[0].ClrType);

        foreach (var entityType in tenantOwned)
        {
            var tenantProperty = entityType.FindProperty(nameof(ITenantOwned.TenantId));
            Assert.NotNull(tenantProperty);
            Assert.False(tenantProperty!.IsNullable);
            Assert.True(tenantProperty.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.Never, tenantProperty.ValueGenerated);
            Assert.NotNull(entityType.GetQueryFilter());
            Assert.Contains(entityType.GetIndexes(), index =>
                index.Properties.Count == 1 && index.Properties[0].Name == nameof(ITenantOwned.TenantId));
        }

        Assert.Null(global[0].GetQueryFilter());
    }

    [Fact]
    public async Task Critical_modules_and_by_id_queries_are_isolated()
    {
        var tenant = new TestTenantContext();
        await using var db = CreateDb(tenant);
        using (tenant.BeginSystemScope())
        {
            db.AddRange(CreateTenantGraph(1, 100).Concat(CreateTenantGraph(2, 200)));
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();

        Assert.Equal([101], await db.Branches.Select(x => x.Id).ToListAsync());
        Assert.Equal([102], await db.Users.Select(x => x.Id).ToListAsync());
        Assert.Equal([103], await db.Customers.Select(x => x.Id).ToListAsync());
        Assert.Equal([104], await db.Addresses.Select(x => x.Id).ToListAsync());
        Assert.Equal([106], await db.Products.Select(x => x.Id).ToListAsync());
        Assert.Equal([107], await db.Orders.Select(x => x.Id).ToListAsync());
        Assert.Equal([109], await db.Expenses.Select(x => x.Id).ToListAsync());
        Assert.Equal([110], await db.WhatsAppConversations.Select(x => x.Id).ToListAsync());
        Assert.Equal([111], await db.StorefrontCheckouts.Select(x => x.Id).ToListAsync());
        Assert.Equal([112], await db.WompiPaymentAttempts.Select(x => x.Id).ToListAsync());
        Assert.Equal([113], await db.DeliveryAppConnections.Select(x => x.Id).ToListAsync());
        Assert.Equal([114L], await db.PrintJobs.Select(x => x.Id).ToListAsync());

        Assert.Null(await db.Customers.FindAsync(203));
        Assert.Null(await db.Orders.SingleOrDefaultAsync(x => x.Id == 207));
        Assert.Null(await db.Users.SingleOrDefaultAsync(x => x.Id == 202));
        Assert.Null(await db.Addresses.SingleOrDefaultAsync(x => x.Id == 204));
        Assert.Null(await db.Products.SingleOrDefaultAsync(x => x.Id == 206));
        Assert.Null(await db.Expenses.SingleOrDefaultAsync(x => x.Id == 209));
        Assert.Null(await db.WhatsAppConversations.SingleOrDefaultAsync(x => x.Id == 210));

        using (tenant.BeginTenantScope(2))
            Assert.Equal([201], await db.Branches.Select(x => x.Id).ToListAsync());

        using (tenant.BeginSystemScope())
            Assert.Equal([101, 201], await db.Branches.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
    }

    [Fact]
    public async Task Write_guards_assign_current_tenant_and_reject_cross_tenant_mutations()
    {
        var tenant = new TestTenantContext();
        await using var db = CreateDb(tenant);
        var assigned = new Branch { Id = 1, Name = "Assigned", Address = "A", Phone1 = "1" };
        db.Add(assigned);
        await db.SaveChangesAsync();
        Assert.Equal(1, assigned.TenantId);

        var wrongInsert = new Branch { Id = 2, TenantId = 2, Name = "Wrong", Address = "B", Phone1 = "2" };
        db.Add(wrongInsert);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(wrongInsert).State = EntityState.Detached;

        db.ChangeTracker.Clear();
        var modified = await db.Branches.SingleAsync(x => x.Id == 1);
        modified.TenantId = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var foreignDelete = new Branch { Id = 20, TenantId = 2, Name = "Foreign", Address = "C", Phone1 = "3" };
        db.Remove(foreignDelete);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Missing_tenant_sees_no_rows_and_cannot_write()
    {
        var databaseName = Guid.NewGuid().ToString();
        var systemTenant = new TestTenantContext();
        await using (var seed = CreateDb(systemTenant, databaseName))
        {
            using var systemScope = systemTenant.BeginSystemScope();
            seed.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "One", Address = "A", Phone1 = "1" });
            await seed.SaveChangesAsync();
        }

        var missingTenant = new TestTenantContext(0);
        await using var db = CreateDb(missingTenant, databaseName);
        Assert.Empty(await db.Branches.ToListAsync());
        db.Branches.Add(new Branch { Id = 2, Name = "No tenant", Address = "B", Phone1 = "2" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Worker_runner_creates_a_fresh_scope_for_each_active_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenant = new TestTenantContext(0);
        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        services.AddSingleton<ICurrentTenant>(tenant);
        services.AddSingleton<ITenantExecutionContext>(tenant);
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        await using var provider = services.BuildServiceProvider();

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            using var systemScope = tenant.BeginSystemScope();
            db.Tenants.AddRange(
                new Tenant { Id = 1, Name = "One", Slug = "one", IsActive = true, Status = TenantStatus.Active },
                new Tenant { Id = 2, Name = "Two", Slug = "two", IsActive = true, Status = TenantStatus.Active },
                new Tenant { Id = 3, Name = "Inactive", Slug = "inactive", IsActive = false, Status = TenantStatus.Suspended });
            db.Branches.AddRange(
                new Branch { Id = 11, TenantId = 1, Name = "One", Address = "A", Phone1 = "1" },
                new Branch { Id = 22, TenantId = 2, Name = "Two", Address = "B", Phone1 = "2" },
                new Branch { Id = 33, TenantId = 3, Name = "Three", Address = "C", Phone1 = "3" });
            await db.SaveChangesAsync();
        }

        var observed = new Dictionary<int, int[]>();
        await TenantWorkerRunner.RunForEachActiveTenantAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            async (scopedServices, tenantId) =>
            {
                var db = scopedServices.GetRequiredService<IApplicationDbContext>();
                observed[tenantId] = await db.Branches.Select(x => x.TenantId).Distinct().ToArrayAsync();
            },
            default);

        Assert.Equal([1, 2], observed.Keys.Order().ToArray());
        Assert.Equal([1], observed[1]);
        Assert.Equal([2], observed[2]);
    }

    private static object[] CreateTenantGraph(int tenantId, int offset)
    {
        return
        [
            new Branch { Id = offset + 1, TenantId = tenantId, Name = $"Branch {tenantId}", Address = "A", Phone1 = "1" },
            new User { Id = offset + 2, TenantId = tenantId, BranchId = offset + 1, Name = "User", Email = $"u{tenantId}@test.local", Phone = "1", PasswordHash = "x" },
            new Customer { Id = offset + 3, TenantId = tenantId, BranchId = offset + 1, Name = "Customer", Phone1 = $"300000000{tenantId}" },
            new Address { Id = offset + 4, TenantId = tenantId, CustomerId = offset + 3, AddressText = "Address" },
            new ProductCategory { Id = offset + 5, TenantId = tenantId, BranchId = offset + 1, Name = "Category" },
            new Product { Id = offset + 6, TenantId = tenantId, CategoryId = offset + 5, Name = "Product" },
            new Order { Id = offset + 7, TenantId = tenantId, BranchId = offset + 1, TakenById = offset + 2, Type = OrderType.Onsite },
            new ExpenseCategory { Id = offset + 8, TenantId = tenantId, Name = "Expense category" },
            new Expense { Id = offset + 9, TenantId = tenantId, CategoryId = offset + 8, Name = "Expense" },
            new WhatsAppConversation { Id = offset + 10, TenantId = tenantId, BranchId = offset + 1, PhoneNumber = $"57300000000{tenantId}" },
            new StorefrontCheckout { Id = offset + 11, TenantId = tenantId, BranchId = offset + 1, PublicId = $"checkout-{tenantId}", IdempotencyKey = $"key-{tenantId}", CustomerPhone = "1", CustomerName = "Customer", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new WompiPaymentAttempt { Id = offset + 12, TenantId = tenantId, IntegrationId = offset + 20, Reference = $"reference-{tenantId}", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new DeliveryAppConnection { Id = offset + 13, TenantId = tenantId, BranchId = offset + 1, FinancialAppId = offset + 30, Provider = "rappi", DisplayName = "Rappi" },
            new PrintJob { Id = offset + 14, TenantId = tenantId, BranchId = offset + 1 }
        ];
    }

    private static ApplicationDbContext CreateDb(TestTenantContext tenant, string? databaseName = null)
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .Options,
            currentTenant: tenant,
            tenantExecutionContext: tenant);
}
