using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

/// <summary>
/// M10-F01: the optimized first-page search must preserve the exact PagedResult
/// contract while skipping a redundant SQL COUNT only for an incomplete page 1.
/// </summary>
public class OrderSearchFirstPageCountTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
    }

    private static async Task<(int populated, int empty)> SeedAsync(ApplicationDbContext context)
    {
        var now = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc);
        var populated = new Branch
        {
            Name = "Sucursal con pedidos", Address = "Prueba", Phone1 = "0",
            CreatedAt = now
        };
        var empty = new Branch
        {
            Name = "Sucursal vacía", Address = "Prueba", Phone1 = "1",
            CreatedAt = now
        };
        // Relational tests need a parent tenant for the branch and order FKs.
        context.Tenants.Add(new Tenant { Id = 1, Name = "Perf tests", Slug = "perf-tests" });
        await context.SaveChangesAsync();
        context.Branches.AddRange(populated, empty);
        await context.SaveChangesAsync();

        var cashier = new User
        {
            Name = "Cajero",
            Email = $"perf_{Guid.NewGuid():N}@example.com",
            PasswordHash = "test-only",
            Role = UserRole.Cashier,
            BranchId = populated.Id,
            Active = true,
            CreatedAt = now
        };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
        {
            context.Orders.Add(new Order
            {
                BranchId = populated.Id,
                TakenById = cashier.Id,
                Type = OrderType.Onsite,
                Status = OrderStatus.Taken,
                Subtotal = 10000,
                Total = 10000,
                StatusTimes = "{}",
                CreatedAt = now.AddMinutes(-i),
                UpdatedAt = now.AddMinutes(-i)
            });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (populated.Id, empty.Id);
    }

    [Theory]
    [InlineData(1, 10, 3, 1, 3)] // Incomplete first page, count derived from IDs
    [InlineData(1, 2, 3, 2, 2)]  // Full first page, exact COUNT still required
    [InlineData(1, 3, 3, 1, 3)]  // Exactly full page
    [InlineData(2, 2, 3, 2, 1)]  // Later incomplete page, still count
    [InlineData(3, 2, 3, 2, 0)]  // Out-of-range page, still total 3
    public async Task SearchOrders_preserves_total_pages_and_filtered_items(
        int page, int pageSize, int expectedTotal, int expectedPages, int expectedItems)
    {
        await using var context = CreateContext();
        var (populated, empty) = await SeedAsync(context);
        var repository = new OrderRepository(context,
            new FakeClock(new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc)));

        var result = await repository.SearchOrdersAsync(
            branchId: populated, page: page, pageSize: pageSize);

        Assert.Equal(expectedTotal, result.TotalCount);
        Assert.Equal(expectedPages, result.TotalPages);
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.Equal(expectedItems, result.Items.Count());
        Assert.All(result.Items, order => Assert.Equal(populated, order.BranchId));

        var emptyResult = await repository.SearchOrdersAsync(
            branchId: empty, page: 1, pageSize: 100);
        Assert.Empty(emptyResult.Items);
        Assert.Equal(0, emptyResult.TotalCount);
        Assert.Equal(0, emptyResult.TotalPages);
    }

    private sealed class OrderCountCommandInterceptor : DbCommandInterceptor
    {
        private int _countCommands;
        public int CountCommands => Volatile.Read(ref _countCommands);
        public void Reset() => Interlocked.Exchange(ref _countCommands, 0);

        private static bool IsCountQuery(DbCommand command) =>
            Regex.IsMatch(command.CommandText, @"^\s*SELECT\s+count\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // EF Core executes CountAsync as a relational reader query.
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsCountQuery(command))
                Interlocked.Increment(ref _countCommands);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Unlike the InMemory tests, this validates the SQL optimization itself
    /// against real PostgreSQL: the test must fail if CountAsync is restored
    /// unconditionally, even when pagination totals remain correct.
    /// </summary>
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task SearchOrders_count_query_executes_only_when_total_is_not_known()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        var interceptor = new OrderCountCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        await context.Database.EnsureCreatedAsync();
        var (populated, empty) = await SeedAsync(context);
        var repository = new OrderRepository(context,
            new FakeClock(new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc)));

        async Task AssertSearchAsync(int branchId, int page, int size,
            int expectedTotal, int expectedRows, int expectedCounts)
        {
            interceptor.Reset();
            var result = await repository.SearchOrdersAsync(
                branchId: branchId, page: page, pageSize: size);
            Assert.Equal(expectedTotal, result.TotalCount);
            Assert.Equal(expectedRows, result.Items.Count());
            Assert.Equal(expectedCounts, interceptor.CountCommands);
        }

        await AssertSearchAsync(populated, page: 1, size: 10,
            expectedTotal: 3, expectedRows: 3, expectedCounts: 0);
        await AssertSearchAsync(empty, page: 1, size: 10,
            expectedTotal: 0, expectedRows: 0, expectedCounts: 0);
        await AssertSearchAsync(populated, page: 1, size: 3,
            expectedTotal: 3, expectedRows: 3, expectedCounts: 1);
        await AssertSearchAsync(populated, page: 1, size: 2,
            expectedTotal: 3, expectedRows: 2, expectedCounts: 1);
        await AssertSearchAsync(populated, page: 2, size: 2,
            expectedTotal: 3, expectedRows: 1, expectedCounts: 1);
        await AssertSearchAsync(populated, page: 9, size: 2,
            expectedTotal: 3, expectedRows: 0, expectedCounts: 1);
    }
}
