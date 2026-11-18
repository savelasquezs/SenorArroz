using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

/// <summary>Runs only against a disposable PostgreSQL database; never against production.</summary>
public sealed class DashboardHourlySqlAggregationPostgreSqlTests : IAsyncLifetime
{
    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private PostgreSqlContainer? _postgres;
    private string? _serverConnectionString;
    private string? _temporaryDatabase;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private readonly SqlCapture _capture = new();

    private ApplicationDbContext Db() => new(_options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);

    public async Task InitializeAsync()
    {
        var shared = Environment.GetEnvironmentVariable("FLOW_POSTGRES_TEST_CONNECTION");
        string connection;
        if (!string.IsNullOrWhiteSpace(shared))
        {
            var admin = new NpgsqlConnectionStringBuilder(shared) { Database = "postgres", Pooling = false };
            if (admin.Host is not ("localhost" or "127.0.0.1"))
                throw new InvalidOperationException("Dashboard integration tests require isolated localhost PostgreSQL.");
            _serverConnectionString = admin.ConnectionString;
            _temporaryDatabase = "dashboard_hourly_test_" + Guid.NewGuid().ToString("N");
            await using var server = new NpgsqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {_temporaryDatabase}", server);
            await create.ExecuteNonQueryAsync();
            admin.Database = _temporaryDatabase;
            connection = admin.ConnectionString;
        }
        else
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
            connection = _postgres.GetConnectionString();
        }

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(_capture)
            .Options;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_serverConnectionString is not null && _temporaryDatabase is not null)
        {
            await using var server = new NpgsqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_temporaryDatabase} WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync();
        }
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Hourly_dashboard_groups_in_sql_with_colombia_midnight_and_status_filters()
    {
        await using var db = Db();
        var tenant = new Tenant { Id = 1, Name = "Hourly test", Slug = "hourly-test" };
        var first = new Branch { Name = "A", Address = "A", Phone1 = "1" };
        var other = new Branch { Name = "B", Address = "B", Phone1 = "2" };
        var user = new User { Name = "Test user", Email = "hourly@test.local" };
        db.Tenants.Add(tenant);
        db.Branches.AddRange(first, other);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        static DateTime Co(int day, int hour, int minute = 0) =>
            ColombiaTimeHelper.ConvertColombiaToUtc(new DateTime(2026, 7, day, hour, minute, 0));
        Order Make(Branch branch, DateTime created, int total,
            OrderStatus status = OrderStatus.Taken, DateTime? prepare = null) => new()
        {
            BranchId = branch.Id, TakenById = user.Id, Type = OrderType.Onsite,
            Status = status, CreatedAt = created, PrepareAt = prepare,
            UpdatedAt = created, Total = total, Subtotal = total,
        };

        db.Orders.AddRange(
            Make(first, Co(2, 11, 10), 100),
            Make(first, Co(2, 11, 30), 200),
            Make(first, Co(1, 18), 500, prepare: Co(2, 11, 45)),
            Make(first, Co(2, 23, 59), 600),
            Make(first, Co(3, 0, 1), 700),
            Make(first, Co(3, 11), 400),
            Make(first, Co(2, 11), 999, status: OrderStatus.Cancelled),
            Make(first, Co(2, 11), 888, status: OrderStatus.AwaitingPayment),
            Make(other, Co(2, 11), 777));
        await db.SaveChangesAsync();

        var (from, to) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(
            new DateTime(2026, 7, 2), new DateTime(2026, 7, 3));

        // The result must not depend on PostgreSQL session timezone.
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Asia/Tokyo'");
        _capture.Statements.Clear();
        var buckets = await new OrderRepository(db, new SystemUtcClock())
            .GetDashboardSalesDailyHourBucketsAsync(first.Id, from, to);

        // Exactly one grouped result query, not one row transferred per order.
        var query = Assert.Single(_capture.Statements);
        Assert.Contains("GROUP BY", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AT TIME ZONE", query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, buckets.Count);
        Assert.Equal(6, buckets.Sum(x => x.OrderCount));
        var day2Morning = Assert.Single(buckets, x => x.Day == new DateTime(2026, 7, 2) && x.Hour == 11);
        Assert.Equal(3, day2Morning.OrderCount);
        Assert.Equal(800L, day2Morning.TotalSalesCop);
        Assert.Equal(4, day2Morning.DayOfWeek);
        Assert.Equal(600L, Assert.Single(buckets, x => x.Day == new DateTime(2026, 7, 2) && x.Hour == 23).TotalSalesCop);
        Assert.Equal(700L, Assert.Single(buckets, x => x.Day == new DateTime(2026, 7, 3) && x.Hour == 0).TotalSalesCop);
        Assert.Equal(400L, Assert.Single(buckets, x => x.Day == new DateTime(2026, 7, 3) && x.Hour == 11).TotalSalesCop);
    }
}
