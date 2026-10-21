using System.Data.Common;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Application.Features.Branches.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Shared.Models;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace SenorArroz.Tests;

public sealed class BranchListBatchStatisticsTests(ITestOutputHelper output)
{
    private static readonly BranchListStatistics Zero = new(0, 0, 0, 0, 0);

    private static ApplicationDbContext CreateDb(TestTenantContext tenant,
        DbContextOptions<ApplicationDbContext>? options = null) => new(
        options ?? new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: tenant, tenantExecutionContext: tenant);

    private static async Task SeedAsync(ApplicationDbContext db, TestTenantContext tenant)
    {
        using var scope = tenant.BeginSystemScope();
        db.Tenants.AddRange(
            new Tenant { Id = 1, Name = "Batch tests", Slug = "batch-tests" },
            new Tenant { Id = 2, Name = "Other tenant", Slug = "other-batch-tests" });
        db.Branches.AddRange(Enumerable.Range(1, 11).Select(id => new Branch
        {
            Id = id, TenantId = id == 11 ? 2 : 1,
            Name = $"Test branch {id}", Address = "Test", Phone1 = id.ToString()
        }));
        db.Users.AddRange(
            new User { Id = 101, TenantId = 1, BranchId = 1, Name = "Active", Active = true,
                Email = "active@example.invalid", PasswordHash = "test-only", Role = UserRole.Cashier },
            new User { Id = 102, TenantId = 1, BranchId = 1, Name = "Inactive", Active = false,
                Email = "inactive@example.invalid", PasswordHash = "test-only", Role = UserRole.Cashier },
            new User { Id = 111, TenantId = 2, BranchId = 11, Name = "Other tenant", Active = true,
                Email = "other@example.invalid", PasswordHash = "test-only", Role = UserRole.Cashier });
        db.Customers.AddRange(
            new Customer { Id = 201, TenantId = 1, BranchId = 1, Name = "Active", Active = true },
            new Customer { Id = 202, TenantId = 1, BranchId = 1, Name = "Inactive", Active = false },
            new Customer { Id = 211, TenantId = 2, BranchId = 11, Name = "Other tenant", Active = true });
        db.Neighborhoods.AddRange(
            new Neighborhood { Id = 301, TenantId = 1, BranchId = 1, Name = "Active", Active = true },
            new Neighborhood { Id = 302, TenantId = 1, BranchId = 1, Name = "Inactive", Active = false },
            new Neighborhood { Id = 311, TenantId = 2, BranchId = 11, Name = "Other tenant", Active = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Batch_preserves_totals_and_tenant_filters_for_duplicate_and_missing_ids()
    {
        var tenant = new TestTenantContext(1);
        await using var db = CreateDb(tenant);
        await SeedAsync(db, tenant);
        var repo = new BranchRepository(db, Mock.Of<IClock>());
        var values = await repo.GetListStatisticsForBranchesAsync([1, 2, 11, 999, 1]);
        Assert.Equal(new BranchListStatistics(2, 1, 2, 1, 2), values[1]);
        Assert.Equal(Zero, values[2]);
        Assert.Equal(Zero, values[11]);
        Assert.Equal(Zero, values[999]);
        Assert.Equal(4, values.Count);
        using (tenant.BeginTenantScope(2))
        {
            values = await repo.GetListStatisticsForBranchesAsync([1, 11]);
            Assert.Equal(Zero, values[1]);
            Assert.Equal(new BranchListStatistics(1, 1, 1, 1, 1), values[11]);
        }
    }

    [Fact]
    public async Task Empty_input_returns_empty_and_cancelled_input_is_not_executed()
    {
        var tenant = new TestTenantContext(1);
        await using var db = CreateDb(tenant);
        var repo = new BranchRepository(db, Mock.Of<IClock>());
        Assert.Empty(await repo.GetListStatisticsForBranchesAsync([]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repo.GetListStatisticsForBranchesAsync([], new CancellationToken(true)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Handler_batches_only_page_ids_and_propagates_cancellation(int branchCount)
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var branches = Enumerable.Range(1, branchCount)
            .Select(id => new Branch { Id = id, Name = $"Branch {id}" }).ToList();
        var repo = new Mock<IBranchRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetPagedAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), token))
            .ReturnsAsync(new PagedResult<Branch>
            {
                Items = branches, TotalCount = 12, Page = 2, PageSize = 10, TotalPages = 2
            });
        repo.Setup(r => r.GetListStatisticsForBranchesAsync(
                It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(branches.Select(b => b.Id))), token))
            .ReturnsAsync(branches.ToDictionary(b => b.Id, _ => new BranchListStatistics(2, 1, 3, 2, 4)));
        var mapper = new Mock<IMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<BranchDto>(It.IsAny<object>()))
            .Returns((object entity) => new BranchDto { Id = ((Branch)entity).Id });

        var result = await new GetBranchesHandler(repo.Object, mapper.Object)
            .Handle(new GetBranchesQuery(), token);

        Assert.Equal((12, 2, 10, 2), (result.TotalCount, result.Page, result.PageSize, result.TotalPages));
        Assert.Equal(branchCount, result.Items.Count());
        Assert.All(result.Items, row => Assert.Equal((2, 1, 3, 2, 4),
            (row.TotalUsers, row.ActiveUsers, row.TotalCustomers, row.ActiveCustomers, row.TotalNeighborhoods)));
        repo.VerifyAll();
        repo.VerifyNoOtherCalls(); // Legacy per-branch COUNT methods are not allowed.
    }

    private sealed class ReadCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PostgreSql_batch_replaces_50_counts_with_3_grouped_reads_and_keeps_tenant_filters()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var tenant = new TestTenantContext(1);
        var reads = new ReadCommands();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(reads).Options;
        await using var db = CreateDb(tenant, options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db, tenant);
        var repo = new BranchRepository(db, Mock.Of<IClock>());
        var pageIds = Enumerable.Range(1, 10).ToArray();

        reads.Sql.Clear();
        var before = new Dictionary<int, BranchListStatistics>();
        foreach (var id in pageIds)
            before[id] = new BranchListStatistics(
                await repo.GetTotalUsersAsync(id), await repo.GetActiveUsersAsync(id),
                await repo.GetTotalCustomersAsync(id), await repo.GetActiveCustomersAsync(id),
                await repo.GetTotalNeighborhoodsAsync(id));
        Assert.Equal(50, reads.Sql.Count);

        reads.Sql.Clear();
        var after = await repo.GetListStatisticsForBranchesAsync(pageIds);
        Assert.Equal(3, reads.Sql.Count);
        Assert.All(reads.Sql, sql => Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before.Count, after.Count);
        foreach (var id in pageIds) Assert.Equal(before[id], after[id]);

        reads.Sql.Clear();
        Assert.Empty(await repo.GetListStatisticsForBranchesAsync([]));
        Assert.Empty(reads.Sql);

        reads.Sql.Clear();
        var one = await repo.GetListStatisticsForBranchesAsync([1, 1]);
        Assert.Single(one);
        Assert.Equal(before[1], one[1]);
        Assert.Equal(3, reads.Sql.Count);
        Assert.Equal(Zero, (await repo.GetListStatisticsForBranchesAsync([11]))[11]);
        using (tenant.BeginTenantScope(2))
        {
            var other = await repo.GetListStatisticsForBranchesAsync([1, 11]);
            Assert.Equal(Zero, other[1]);
            Assert.Equal(new BranchListStatistics(1, 1, 1, 1, 1), other[11]);
        }
        // EnsureCreated does not install production RLS policies. This test verifies EF filters;
        // the existing MultitenantRlsPostgreSqlTests suite remains the RLS regression check.
        output.WriteLine("Isolated PostgreSQL: 10 branches, 50 legacy COUNTs -> 3 grouped SELECTs; " +
            "1 branch -> 3 SELECTs; empty input -> 0; tenant-filtered totals unchanged.");
    }
}
