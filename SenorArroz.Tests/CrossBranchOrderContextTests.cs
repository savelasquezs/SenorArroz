using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SenorArroz.Application.Features.Branches.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace SenorArroz.Tests;

public sealed class CrossBranchOrderContextTests(ITestOutputHelper output)
{
    private static ApplicationDbContext CreateDb(
        TestTenantContext tenant, DbContextOptions<ApplicationDbContext>? options = null) =>
        new(options ?? new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: tenant, tenantExecutionContext: tenant);

    private static async Task SeedAsync(ApplicationDbContext db, TestTenantContext tenant)
    {
        using (tenant.BeginSystemScope())
        {
            db.Tenants.AddRange(
                new Tenant { Id = 1, Name = "Tenant one", Slug = "order-scope-one" },
                new Tenant { Id = 2, Name = "Tenant two", Slug = "order-scope-two" });
            db.Branches.AddRange(
                new Branch { Id = 1, TenantId = 1, Name = "Assigned branch", Address = "Test 1" },
                new Branch
                {
                    Id = 2, TenantId = 1, Name = "Other branch", Address = "Test 2",
                    Latitude = 6.25m, Longitude = -75.56m,
                    MaxFreeDeliveryDiscount = 4200,
                    PosCopyEtaMinMinutes = 42, PosCopyEtaRangeMinutes = 12
                },
                new Branch { Id = 3, TenantId = 2, Name = "Other tenant", Address = "Test 3" });
            db.Users.Add(new User
            {
                Id = 201, TenantId = 1, BranchId = 2, Name = "Private test user",
                Email = "private@example.invalid", Phone = "000",
                PasswordHash = "synthetic-only", Role = UserRole.Cashier, Active = true
            });
            db.Neighborhoods.Add(new Neighborhood
            {
                Id = 301, TenantId = 1, BranchId = 2, Name = "Synthetic zone", Active = true
            });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Other_branch_returns_only_order_fields_and_no_personnel_or_admin_data()
    {
        var tenant = new TestTenantContext(1);
        await using var db = CreateDb(tenant);
        await SeedAsync(db, tenant);
        var dto = await new GetBranchOrderContextHandler(db)
            .Handle(new GetBranchOrderContextQuery(2), CancellationToken.None);
        Assert.NotNull(dto);
        Assert.Equal((2, "Other branch", "Test 2"), (dto.Id, dto.Name, dto.Address));
        Assert.Equal((6.25m, -75.56m), (dto.Latitude, dto.Longitude));
        Assert.Equal((4200, 42, 12), (dto.MaxFreeDeliveryDiscount,
            dto.PosCopyEtaMinMinutes, dto.PosCopyEtaRangeMinutes));
        Assert.Empty(dto.Users);
        Assert.Empty(dto.Neighborhoods);
        Assert.Null(dto.PrintSettings);
        Assert.Equal(0, dto.TotalUsers);
        Assert.Equal(0, dto.TotalCustomers);
        Assert.Equal(0, dto.TotalNeighborhoods);
    }

    [Fact]
    public async Task Tenant_filters_hide_foreign_branches_and_missing_ids()
    {
        var tenant = new TestTenantContext(1);
        await using var db = CreateDb(tenant);
        await SeedAsync(db, tenant);
        var handler = new GetBranchOrderContextHandler(db);
        Assert.Null(await handler.Handle(new GetBranchOrderContextQuery(3), CancellationToken.None));
        Assert.Null(await handler.Handle(new GetBranchOrderContextQuery(999), CancellationToken.None));
        using (tenant.BeginTenantScope(2))
        {
            Assert.Null(await handler.Handle(new GetBranchOrderContextQuery(2), CancellationToken.None));
            Assert.Equal(3, (await handler.Handle(new GetBranchOrderContextQuery(3), CancellationToken.None))?.Id);
        }
    }

    [Fact]
    public async Task Cancelled_request_is_not_executed()
    {
        var tenant = new TestTenantContext(1);
        await using var db = CreateDb(tenant);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GetBranchOrderContextHandler(db).Handle(
                new GetBranchOrderContextQuery(1), new CancellationToken(true)));
    }

    private sealed class SelectCounter : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task PostgreSql_projects_one_branch_row_in_one_select_without_joining_personnel()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var tenant = new TestTenantContext(1);
        var queries = new SelectCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(queries).Options;
        await using var db = CreateDb(tenant, options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db, tenant);

        queries.Reads.Clear();
        var dto = await new GetBranchOrderContextHandler(db)
            .Handle(new GetBranchOrderContextQuery(2), CancellationToken.None);
        Assert.NotNull(dto);
        Assert.Equal(4200, dto.MaxFreeDeliveryDiscount);
        Assert.Single(queries.Reads);
        var sql = queries.Reads[0];
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Neighborhoods", sql, StringComparison.OrdinalIgnoreCase);

        queries.Reads.Clear();
        Assert.Null(await new GetBranchOrderContextHandler(db)
            .Handle(new GetBranchOrderContextQuery(3), CancellationToken.None));
        Assert.Single(queries.Reads);
        output.WriteLine("Order-creation branch context: one projected SELECT, no user/zone joins; tenant filter preserved.");
        // EnsureCreated does not install production RLS policies. Tenant EF filters are
        // verified here; MultitenantRlsPostgreSqlTests independently cover RLS.
    }
}
