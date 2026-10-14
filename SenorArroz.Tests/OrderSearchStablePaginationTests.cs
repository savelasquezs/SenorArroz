using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>M10-F02: deterministic OFFSET/LIMIT pagination when primary sort keys tie.</summary>
public class OrderSearchStablePaginationTests
{
    private static readonly DateTime TestNow = new(2026, 10, 14, 13, 0, 0, DateTimeKind.Utc);

    private static async Task<(int BranchId, int[] OrderIds)> SeedTiedOrdersAsync(ApplicationDbContext context)
    {
        context.Tenants.Add(new Tenant { Id = 1, Name = "Pagination tests", Slug = "pagination-tests" });
        await context.SaveChangesAsync();
        var branch = new Branch { Name = "Pagination branch", Address = "Test", Phone1 = "0", CreatedAt = TestNow };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();
        var user = new User
        {
            Name = "Cashier", Email = $"paging_{Guid.NewGuid():N}@example.com",
            PasswordHash = "test-only", Role = UserRole.Cashier,
            BranchId = branch.Id, Active = true, CreatedAt = TestNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Primary sort values coincide for all orders, including nullable dates.
        for (var i = 0; i < 4; i++)
        {
            context.Orders.Add(new Order
            {
                BranchId = branch.Id, TakenById = user.Id, Type = OrderType.Onsite,
                Status = OrderStatus.Taken, Subtotal = 10000, Total = 10000,
                StatusTimes = "{}", CreatedAt = TestNow, UpdatedAt = TestNow
            });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var ids = await context.Orders.AsNoTracking()
            .Where(o => o.BranchId == branch.Id)
            .Select(o => o.Id).OrderBy(id => id).ToArrayAsync();
        Assert.Equal(4, ids.Length);
        return (branch.Id, ids);
    }

    [Theory]
    [InlineData("createdAt", "asc")]
    [InlineData("createdAt", "desc")]
    [InlineData("updatedAt", "desc")]
    [InlineData("status", "desc")]
    [InlineData("type", "asc")]
    [InlineData("total", "desc")]
    [InlineData("subtotal", "asc")]
    [InlineData("prepareAt", "desc")]
    [InlineData("reservedFor", "asc")]
    [InlineData("invalidSort", "desc")]
    public async Task Tied_values_have_stable_sort_and_no_overlap_between_pages(
        string sortBy, string sortOrder)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var context = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        var (branchId, ids) = await SeedTiedOrdersAsync(context);
        var repository = new OrderRepository(context, new FakeClock(TestNow));

        var first = await repository.SearchOrdersAsync(
            branchId: branchId, sortBy: sortBy, sortOrder: sortOrder, page: 1, pageSize: 2);
        var second = await repository.SearchOrdersAsync(
            branchId: branchId, sortBy: sortBy, sortOrder: sortOrder, page: 2, pageSize: 2);

        Assert.Equal(4, first.TotalCount);
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, second.TotalPages);
        var actualIds = first.Items.Concat(second.Items).Select(o => o.Id).ToArray();
        var expectedIds = sortOrder == "desc" ? ids.Reverse().ToArray() : ids;
        Assert.Equal(expectedIds, actualIds);
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PostgreSql_id_page_query_orders_by_total_then_unique_id()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var capture = new SqlCapture();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(capture).Options;
        await using var context = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        await context.Database.EnsureCreatedAsync();
        var (branchId, ids) = await SeedTiedOrdersAsync(context);
        capture.Commands.Clear();
        var repository = new OrderRepository(context, new FakeClock(TestNow));

        var result = await repository.SearchOrdersAsync(
            branchId: branchId, sortBy: "total", sortOrder: "desc", page: 1, pageSize: 2);

        Assert.Equal(ids.Reverse().Take(2), result.Items.Select(o => o.Id));
        var pageSql = Assert.Single(capture.Commands.Where(sql =>
            sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)));
        var normalizedSql = pageSql.Replace("\"", string.Empty, StringComparison.Ordinal);
        Assert.Matches(@"(?is)\bORDER BY\s+\w+\.total\s+DESC\s*,\s*\w+\.id\s+DESC", normalizedSql);
    }
}
