using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Printing;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class MultitenantRlsPostgreSqlTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _adminConnectionString = null!;
    private string _runtimeConnectionString = null!;
    private string? _serverConnectionString;
    private string? _temporaryDatabaseName;
    private readonly string _runtimeRole = $"app_runtime_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            var sharedConnectionString = Environment.GetEnvironmentVariable("FLOW_POSTGRES_TEST_CONNECTION");
            if (!string.IsNullOrWhiteSpace(sharedConnectionString))
                await CreateIsolatedDatabaseAsync(sharedConnectionString);
            else
            {
                _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await _postgres.StartAsync();
                _adminConnectionString = _postgres.GetConnectionString();
            }
        }
        var adminOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_adminConnectionString).Options;
        var tenant = new TestTenantContext();
        await using (var db = new ApplicationDbContext(adminOptions, currentTenant: tenant, tenantExecutionContext: tenant))
        {
            await db.Database.EnsureCreatedAsync();
            using var systemScope = tenant.BeginSystemScope();
            db.Tenants.AddRange(
                new Tenant { Id = 1, Name = "Tenant One", Slug = "tenant-one" },
                new Tenant { Id = 2, Name = "Tenant Two", Slug = "tenant-two" });
            db.Branches.AddRange(
                new Branch { Id = 101, TenantId = 1, Name = "Branch One", Address = "A", Phone1 = "3000000001" },
                new Branch { Id = 202, TenantId = 2, Name = "Branch Two", Address = "B", Phone1 = "3000000002" });
            await db.SaveChangesAsync();
        }

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, ReadScript("tenant_scoped_unique_indexes_v3.sql"));
        await ExecuteAsync(admin, ReadScript("enable_multitenant_rls_v3.sql"));
        await ExecuteAsync(admin, $"""
            CREATE ROLE {_runtimeRole} LOGIN PASSWORD 'runtime-test-password' NOSUPERUSER NOBYPASSRLS;
            GRANT CONNECT ON DATABASE {admin.Database} TO {_runtimeRole};
            GRANT USAGE ON SCHEMA public, app TO {_runtimeRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {_runtimeRole};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {_runtimeRole};
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA app TO {_runtimeRole};
            """);

        _runtimeConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Username = _runtimeRole,
            Password = "runtime-test-password",
            Pooling = true,
            NoResetOnClose = false
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (_serverConnectionString is not null && _temporaryDatabaseName is not null)
        {
            await using var server = new NpgsqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {_temporaryDatabaseName} WITH (FORCE)");
            await ExecuteAsync(server, $"DROP ROLE IF EXISTS {_runtimeRole}");
        }
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private async Task CreateIsolatedDatabaseAsync(string sharedConnectionString)
    {
        _temporaryDatabaseName = $"rls_test_{Guid.NewGuid():N}";
        var serverBuilder = new NpgsqlConnectionStringBuilder(sharedConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        _serverConnectionString = serverBuilder.ConnectionString;
        await using var server = new NpgsqlConnection(_serverConnectionString);
        await server.OpenAsync();
        await ExecuteAsync(server, $"CREATE DATABASE {_temporaryDatabaseName}");
        serverBuilder.Database = _temporaryDatabaseName;
        _adminConnectionString = serverBuilder.ConnectionString;
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Rls_enforces_crud_system_scope_transactions_pool_reset_and_tenant_unique_indexes()
    {
        await using (var connection = new NpgsqlConnection(_runtimeConnectionString))
        {
            await connection.OpenAsync();
            await SetContextAsync(connection, "1", "off");
            var tenantOneBranches = await BranchIdsAsync(connection);
            Assert.Equal(new[] { 101 }, tenantOneBranches);

            var insert = new NpgsqlCommand("INSERT INTO branch (tenant_id, name, address, phone1) VALUES (2, 'Rejected', 'X', '3000000099')", connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);

            await using var transaction = await connection.BeginTransactionAsync();
            await SetContextAsync(connection, "1", "off", transaction);
            var update = new NpgsqlCommand("UPDATE branch SET name = 'Hidden' WHERE id = 202", connection, transaction);
            Assert.Equal(0, await update.ExecuteNonQueryAsync());
            var delete = new NpgsqlCommand("DELETE FROM branch WHERE id = 202", connection, transaction);
            Assert.Equal(0, await delete.ExecuteNonQueryAsync());
            await transaction.RollbackAsync();

            await SetContextAsync(connection, "", "on");
            var systemBranches = await BranchIdsAsync(connection);
            Assert.Equal(new[] { 101, 202 }, systemBranches);
        }

        await using (var pooled = new NpgsqlConnection(_runtimeConnectionString))
        {
            await pooled.OpenAsync();
            var tenantSetting = await new NpgsqlCommand("SELECT current_setting('app.current_tenant_id', true)", pooled).ExecuteScalarAsync();
            var systemSetting = await new NpgsqlCommand("SELECT current_setting('app.system_scope', true)", pooled).ExecuteScalarAsync();
            Assert.True(tenantSetting is null or "");
            Assert.True(systemSetting is null or "" or "off");
            Assert.Empty(await BranchIdsAsync(pooled));
        }

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        var indexDefinition = (string?)await new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_storefront_checkout_idempotency_key'", admin).ExecuteScalarAsync();
        Assert.Contains("tenant_id", indexDefinition, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Print_agent_claims_by_SignalR_id_with_RLS_and_isolates_other_tenants()
    {
        const string token = "print-agent-integration-token";
        const string salt = "print-test-salt";
        long ownJobId;
        long secondJobId;
        long foreignJobId;
        var adminOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_adminConnectionString).Options;
        var tenant = new TestTenantContext(1);

        // Datos aislados por tenant, creados con el rol administrador de esta BD de pruebas.
        await using (var setup = new ApplicationDbContext(
            adminOptions, currentTenant: tenant, tenantExecutionContext: tenant))
        {
            using var systemScope = tenant.BeginSystemScope();
            setup.BranchPrintSettings.Add(new BranchPrintSettings
            {
                BranchId = 101,
                TenantId = 1,
                AgentTokenHash = PrintAgentTokenCrypto.ComputeHash(salt, token),
                AgentTokenSalt = salt,
            });
            var ownJob = new PrintJob
            {
                TenantId = 1, BranchId = 101, Kind = PrintJobKind.Kitchen,
                Status = PrintJobStatus.Pending, OrderIdsJson = "[]", PayloadJson = "{}",
            };
            var secondJob = new PrintJob
            {
                TenantId = 1, BranchId = 101, Kind = PrintJobKind.Kitchen,
                Status = PrintJobStatus.Pending, OrderIdsJson = "[]", PayloadJson = "{}",
            };
            var foreignJob = new PrintJob
            {
                TenantId = 2, BranchId = 202, Kind = PrintJobKind.Kitchen,
                Status = PrintJobStatus.Pending, OrderIdsJson = "[]", PayloadJson = "{}",
            };
            setup.PrintJobs.AddRange(ownJob, secondJob, foreignJob);
            await setup.SaveChangesAsync();
            ownJobId = ownJob.Id;
            secondJobId = secondJob.Id;
            foreignJobId = foreignJob.Id;
        }

        // Igual que producción: rol sin BYPASSRLS e interceptores de conexión/comando.
        var runtimeOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_runtimeConnectionString)
            .AddInterceptors(
                new TenantDbConnectionInterceptor(tenant, tenant),
                new TenantDbCommandInterceptor(tenant, tenant))
            .Options;

        await using var db = new ApplicationDbContext(
            runtimeOptions, currentTenant: tenant, tenantExecutionContext: tenant);
        var service = new PrintQueueService(
            db, Options.Create(new ApiPublicOptions()), Options.Create(new BrandingOptions()),
            Mock.Of<IClock>(), Mock.Of<IPrintAgentNotifier>(), tenant,
            NullLogger<PrintQueueService>.Instance);

        using var tenantScope = tenant.BeginTenantScope(1);
        Assert.Null(await service.AuthenticateAgentAsync(101, "wrong-token"));
        var authenticated = await service.AuthenticateAgentAsync(101, token);
        Assert.NotNull(authenticated);
        Assert.Equal(1, authenticated.TenantId);

        // El claim directo abre la conexión con interceptor: antes se abría por Npgsql
        // y RLS ocultaba la fila. Un segundo claim del mismo ID no debe duplicarse.
        var claimed = await service.ClaimSpecificForAgentAsync(101, ownJobId);
        Assert.NotNull(claimed);
        Assert.Equal(ownJobId, claimed.Id);
        Assert.Null(await service.ClaimSpecificForAgentAsync(101, ownJobId));
        Assert.Null(await service.ClaimSpecificForAgentAsync(202, foreignJobId));

        // El polling de respaldo conserva la búsqueda con SKIP LOCKED.
        var pending = await service.ClaimPendingForAgentAsync(101, [PrintJobKind.Kitchen], 5);
        Assert.Single(pending);
        Assert.Equal(secondJobId, pending[0].Id);
        Assert.Empty(await service.ClaimPendingForAgentAsync(101, [PrintJobKind.Kitchen], 5));
    }

    private static async Task SetContextAsync(NpgsqlConnection connection, string tenantId, string systemScope, NpgsqlTransaction? transaction = null)
    {
        var command = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tenant, false), set_config('app.system_scope', @system, false)", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("system", systemScope);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int[]> BranchIdsAsync(NpgsqlConnection connection)
    {
        var result = new List<int>();
        await using var reader = await new NpgsqlCommand("SELECT id FROM branch ORDER BY id", connection).ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetInt32(0));
        return result.ToArray();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql) =>
        await new NpgsqlCommand(sql, connection) { CommandTimeout = 120 }.ExecuteNonQueryAsync();

    private static string ReadScript(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "SenorArroz.Infrastructure", "Scripts", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
