using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Tests;

[Collection("Gateway database roles")]
public sealed class DataGatewayPostgreSqlTests
{
    [GatewayPostgreSqlFact]
    public async Task Migration_and_gateway_enforce_reader_isolation_limits_and_performance()
    {
        var source = Environment.GetEnvironmentVariable("FLOW_POSTGRES_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(source), "FLOW_POSTGRES_TEST_CONNECTION is required for gateway security verification");
        Assert.Contains(new NpgsqlConnectionStringBuilder(source).Host, new[] { "localhost", "127.0.0.1", "::1", "flow-postgres" });
        var database = "gateway_" + Guid.NewGuid().ToString("N");
        await using var server = new NpgsqlConnection(source);
        await server.OpenAsync();
        await Execute(server, $"CREATE DATABASE {database}");
        var adminOptions = new NpgsqlConnectionStringBuilder(source) { Database = database, Pooling = false };
        var createdReader = false;
        try
        {
            await using var admin = new NpgsqlConnection(adminOptions.ConnectionString);
            await admin.OpenAsync();
            await Execute(admin, "CREATE ROLE senorarroz_db_reader LOGIN PASSWORD 'gateway-test-only-password' NOSUPERUSER NOBYPASSRLS; REVOKE CREATE ON SCHEMA public FROM PUBLIC;");
            createdReader = true;
            var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", "expand_db_reader_diagnostics_grants_20261110.sql"));
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                Regex.Match(sql, "manifest constant jsonb := '(.*?)'::jsonb;", RegexOptions.Singleline).Groups[1].Value)!;
            foreach (var (table, columns) in manifest)
            {
                var definitions = columns.Select(c => $"\"{c}\" " + (c is "id" or "tenant_id" or "branch_id" ? "integer" :
                    c is "created_at" or "updated_at" ? "timestamptz" : c is "total" or "quantity_on_hand" ? "numeric" : "text"));
                await Execute(admin, $"""
                    CREATE TABLE public."{table}" ({string.Join(",", definitions)}, private_secret text);
                    ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_select_policy ON public."{table}" FOR SELECT USING (
                      current_setting('app.system_scope',true)='on' OR tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::integer);
                    INSERT INTO public."{table}" (id,tenant_id,created_at) VALUES (1,1,now()),(2,2,now());
                    GRANT SELECT ON public."{table}" TO senorarroz_db_reader;
                    """);
            }
            await Execute(admin, "UPDATE public.\"order\" SET branch_id=1,total=123.45,status='delivered';");
            await Execute(admin, "ALTER TABLE public.\"order\" NO FORCE ROW LEVEL SECURITY");
            await using (var rejected = await admin.BeginTransactionAsync())
            {
                await Assert.ThrowsAsync<PostgresException>(() => Execute(admin, sql, rejected));
                await rejected.RollbackAsync();
            }
            await Execute(admin, "ALTER TABLE public.\"order\" FORCE ROW LEVEL SECURITY");
            await using (var tx = await admin.BeginTransactionAsync())
            {
                await Execute(admin, sql, tx);
                await Execute(admin, sql, tx);
                await tx.CommitAsync();
            }
            var readerOptions = new NpgsqlConnectionStringBuilder(adminOptions.ConnectionString)
            { Username = "senorarroz_db_reader", Password = "gateway-test-only-password", Pooling = false };
            await using var reader = new NpgsqlConnection(readerOptions.ConnectionString);
            await reader.OpenAsync();
            await Execute(reader, "SET app.current_tenant_id='2'; SET app.system_scope='on';");
            Assert.Equal(1L, await new NpgsqlCommand("SELECT count(*) FROM public.\"order\" WHERE tenant_id=1", reader).ExecuteScalarAsync());
            Assert.Equal(0L, await new NpgsqlCommand("SELECT count(*) FROM public.\"order\" WHERE tenant_id=2", reader).ExecuteScalarAsync());
            Assert.Equal(0L, await new NpgsqlCommand("SELECT count(*) FROM diagnostics.orders_summary", reader).ExecuteScalarAsync());
            foreach (var statement in new[] { "SELECT private_secret FROM public.\"order\"", "SELECT * FROM public.\"order\"",
                "DELETE FROM public.\"order\"", "UPDATE public.\"order\" SET total=0", "CREATE TABLE public.malicious(id int)" })
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => Execute(reader, statement))).SqlState);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataGateway:Enabled"] = "true", ["DataGateway:AllowedViews:0"] = "diagnostics.orders_summary",
                ["DataGateway:AllowedViews:1"] = "diagnostics.order",
                ["ConnectionStrings:DataGatewayReader"] = readerOptions.ConnectionString
            }).Build();
            var tenant = new Mock<ICurrentTenant>();
            tenant.SetupGet(t => t.HasTenant).Returns(true);
            tenant.SetupGet(t => t.TenantId).Returns(1);
            var gateway = new DataGatewayController(config, tenant.Object, NullLogger<DataGatewayController>.Instance)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
            var query = new DataGatewayController.QueryRequest("diagnostics.orders_summary");
            var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Query(query, default)).Value);
            Assert.Single(result.GetProperty("rows").EnumerateArray());
            Assert.Equal("123.45", result.GetProperty("rows")[0].GetProperty("total_cop").GetString());
            Assert.IsType<OkObjectResult>(await gateway.Explain(query, default));
            var dateFilter = new DataGatewayController.Filter("order_date", "gte", JsonSerializer.SerializeToElement("2000-01-01"));
            Assert.IsType<OkObjectResult>(await gateway.Query(query with { Filters = [dateFilter] }, default));
            Assert.IsType<BadRequestObjectResult>(await gateway.Query(query with { Columns = ["total; DROP TABLE x"] }, default));
            Assert.IsType<BadRequestObjectResult>(await gateway.Query(query with { Columns = ["total", "total"] }, default));
            Assert.IsType<BadRequestObjectResult>(await gateway.Query(query with { MaxRows = 501 }, default));
            Assert.IsType<BadRequestObjectResult>(await gateway.Query(query with { Filters = [null!] }, default));
            var malicious = new DataGatewayController.Filter("status", "eq", JsonSerializer.SerializeToElement("';SELECT set_config('app.system_scope','on',true);--"));
            var filtered = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Query(query with { Filters = [malicious] }, default)).Value);
            Assert.Empty(filtered.GetProperty("rows").EnumerateArray());
            tenant.SetupGet(t => t.TenantId).Returns(2);
            Assert.IsType<NotFoundResult>(await gateway.Query(query, default));
            tenant.SetupGet(t => t.TenantId).Returns(1);
            await VerifyUserNames(admin, reader, gateway, config);
            await VerifyLunchReview(admin, reader, gateway, config);
            await Execute(admin, "INSERT INTO public.\"order\" (id,tenant_id,branch_id,created_at,total,status) SELECT i,1,1,now(),1,'delivered' FROM generate_series(3,1003) i");
            var limited = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Query(new("diagnostics.order", MaxRows: 3), default)).Value);
            Assert.Equal(3, limited.GetProperty("rows").GetArrayLength());
            Assert.True(limited.GetProperty("truncated").GetBoolean());
            await Execute(admin, "CREATE VIEW diagnostics.slow WITH (security_invoker=true,security_barrier=true) AS SELECT 1 AS tenant_id FROM pg_sleep(5); GRANT SELECT ON diagnostics.slow TO senorarroz_db_reader;");
            config["DataGateway:AllowedViews:2"] = "diagnostics.slow";
            var timeout = Stopwatch.StartNew();
            Assert.Equal(503, Assert.IsType<ObjectResult>(await gateway.Query(new("diagnostics.slow"), default)).StatusCode);
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(5));
            var blocked = gateway.Query(new("diagnostics.slow"), default);
            var blocked2 = gateway.Query(new("diagnostics.slow"), default);
            await Task.Delay(250);
            Assert.Equal(429, Assert.IsType<StatusCodeResult>(await gateway.Query(query, default)).StatusCode);
            await Task.WhenAll(blocked, blocked2);
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 30; i++) Assert.IsType<OkObjectResult>(await gateway.Query(query, default));
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"30 sequential local reads took {timer.Elapsed}");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Execute(server, $"DROP DATABASE {database} WITH (FORCE)");
            if (createdReader) await Execute(server, "DROP ROLE senorarroz_db_reader");
        }
    }

    private static async Task VerifyUserNames(NpgsqlConnection admin, NpgsqlConnection reader,
        DataGatewayController gateway, IConfiguration config)
    {
        await Execute(admin, """
            CREATE TABLE public."user" (id integer PRIMARY KEY,tenant_id integer NOT NULL,name text NOT NULL,
              email text,password_hash text,phone text,active_session_id uuid);
            ALTER TABLE public."user" ENABLE ROW LEVEL SECURITY;
            CREATE POLICY tenant_select_policy ON public."user" FOR SELECT USING (
              current_setting('app.system_scope',true)='on' OR tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::integer);
            INSERT INTO public."user" (id,tenant_id,name,email,password_hash,phone)
              VALUES (1,1,'José','private@example.test','private-hash','private-phone'),(2,2,'Other tenant',NULL,NULL,NULL);
            """);
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", "add_gateway_user_names_20261110.sql"));
        await using (var rejected = await admin.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<PostgresException>(() => Execute(admin, migration, rejected));
            await rejected.RollbackAsync();
        }
        await Execute(admin, """
            ALTER TABLE public."user" FORCE ROW LEVEL SECURITY;
            CREATE POLICY db_reader_tenant_guard ON public."user" FOR SELECT TO senorarroz_db_reader USING (true);
            """);
        await using (var rejected = await admin.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<PostgresException>(() => Execute(admin, migration, rejected));
            await rejected.RollbackAsync();
        }
        await Execute(admin, "DROP POLICY db_reader_tenant_guard ON public.\"user\"; GRANT SELECT,UPDATE ON public.\"user\" TO senorarroz_db_reader");
        await using (var tx = await admin.BeginTransactionAsync())
        {
            await Execute(admin, migration, tx); await Execute(admin, migration, tx); await tx.CommitAsync();
        }
        await Execute(reader, "SET app.current_tenant_id='2'; SET app.system_scope='on'");
        Assert.Equal(1L, await new NpgsqlCommand("SELECT count(*) FROM public.\"user\"", reader).ExecuteScalarAsync());
        Assert.Equal(0L, await new NpgsqlCommand("SELECT count(*) FROM public.\"user\" WHERE tenant_id=2", reader).ExecuteScalarAsync());
        Assert.Equal(0L, await new NpgsqlCommand("SELECT count(*) FROM diagnostics.users", reader).ExecuteScalarAsync());
        foreach (var sql in new[] { "SELECT * FROM public.\"user\"", "SELECT email FROM public.\"user\"",
            "SELECT password_hash FROM public.\"user\"", "SELECT phone FROM public.\"user\"", "SELECT active_session_id FROM public.\"user\"",
            "UPDATE public.\"user\" SET name='changed'", "DELETE FROM public.\"user\"" })
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => Execute(reader, sql))).SqlState);
        config["DataGateway:AllowedViews:3"] = "diagnostics.users";
        var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Query(new("diagnostics.users"), default)).Value);
        Assert.Single(result.GetProperty("rows").EnumerateArray());
        Assert.Equal(new[] { "id", "name" }, result.GetProperty("rows")[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal("José", result.GetProperty("rows")[0].GetProperty("name").GetString());
        Assert.False(result.GetProperty("truncated").GetBoolean());
        Assert.IsType<BadRequestObjectResult>(await gateway.Query(new("diagnostics.users", Columns: ["email"]), default));
        Assert.IsType<BadRequestObjectResult>(await gateway.Query(new("diagnostics.users", Columns: ["tenant_id"]), default));
        var schema = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Schema(default)).Value);
        var userView = Assert.Single(schema.GetProperty("views").EnumerateArray(), v => v.GetProperty("view").GetString() == "diagnostics.users");
        Assert.Equal(new[] { "id", "name" }, userView.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
    }

    private static async Task VerifyLunchReview(NpgsqlConnection admin, NpgsqlConnection reader,
        DataGatewayController gateway, IConfiguration config)
    {
        await Execute(admin, """
            CREATE TABLE public.employee_lunch (id integer,tenant_id integer,user_id integer,branch_id integer,
              started_at timestamptz,ended_at timestamptz,updated_at timestamptz,started_by_user_id integer);
            ALTER TABLE public.employee_lunch ENABLE ROW LEVEL SECURITY;
            CREATE POLICY tenant_select_policy ON public.employee_lunch FOR SELECT USING (
              current_setting('app.system_scope',true)='on' OR tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::integer);
            INSERT INTO public.employee_lunch VALUES
              (1,1,1,1,'2026-11-10T17:00Z','2026-11-10T18:00Z',now(),1),
              (2,2,2,2,'2026-11-10T17:00Z','2026-11-10T18:00Z',now(),2);
            ALTER TABLE public.delivery_tracking_alert ADD COLUMN deliveryman_id integer,ADD COLUMN incident_id bigint,ADD COLUMN message text;
            ALTER TABLE public.delivery_tracking_incident ADD COLUMN deliveryman_id integer,ADD COLUMN evidence_complete boolean,ADD COLUMN admin_notes text;
            UPDATE public.delivery_tracking_alert SET deliveryman_id=id,incident_id=id,branch_id=1,alert_type='unexpected_stay';
            UPDATE public.delivery_tracking_incident SET deliveryman_id=id,branch_id=1,incident_type='stay',
              started_at='2026-11-10T17:00:00Z',ended_at='2026-11-10T18:00:00Z',evidence_complete=true;
            """);
        var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", "add_gateway_lunch_review_20261110.sql"));
        await using (var rejected = await admin.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<PostgresException>(() => Execute(admin, migration, rejected));
            await rejected.RollbackAsync();
        }
        await Execute(admin, "ALTER TABLE public.employee_lunch FORCE ROW LEVEL SECURITY; GRANT SELECT,UPDATE ON public.employee_lunch TO senorarroz_db_reader");
        await using (var tx = await admin.BeginTransactionAsync())
        {
            await Execute(admin, migration, tx); await Execute(admin, migration, tx); await tx.CommitAsync();
        }
        await Execute(reader, "SET app.current_tenant_id='2'; SET app.system_scope='on'");
        foreach (var table in new[] { "employee_lunch", "delivery_tracking_alert", "delivery_tracking_incident" })
        {
            Assert.Equal(1L, await new NpgsqlCommand($"SELECT count(*) FROM public.{table}", reader).ExecuteScalarAsync());
            Assert.Equal(0L, await new NpgsqlCommand($"SELECT count(*) FROM public.{table} WHERE tenant_id=2", reader).ExecuteScalarAsync());
            Assert.Equal(0L, await new NpgsqlCommand($"SELECT count(*) FROM diagnostics.{table}", reader).ExecuteScalarAsync());
            foreach (var sql in new[] { $"SELECT private_secret FROM public.{table}", $"SELECT * FROM public.{table}",
                $"UPDATE public.{table} SET id=3", $"DELETE FROM public.{table}" })
            {
                if (table == "employee_lunch" && sql.Contains("private_secret")) continue;
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => Execute(reader, sql))).SqlState);
            }
        }
        config["DataGateway:AllowedViews:4"] = "diagnostics.employee_lunch";
        config["DataGateway:AllowedViews:5"] = "diagnostics.delivery_tracking_alert";
        config["DataGateway:AllowedViews:6"] = "diagnostics.delivery_tracking_incident";
        foreach (var (view, columns) in new[] {
            ("employee_lunch", new[] { "id", "user_id", "branch_id", "started_at", "ended_at", "updated_at" }),
            ("delivery_tracking_alert", new[] { "id", "deliveryman_id", "incident_id" }),
            ("delivery_tracking_incident", new[] { "id", "deliveryman_id", "started_at", "ended_at", "evidence_complete" }) })
        {
            var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Query(new DataGatewayController.QueryRequest("diagnostics." + view, Columns: columns), default)).Value);
            Assert.Single(result.GetProperty("rows").EnumerateArray());
            Assert.Equal(columns.OrderBy(n => n).ToArray(), result.GetProperty("rows")[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        }
        foreach (var (view, column) in new[] { ("employee_lunch", "started_by_user_id"),
            ("delivery_tracking_alert", "message"), ("delivery_tracking_incident", "admin_notes") })
        {
            Assert.IsType<BadRequestObjectResult>(await gateway.Query(new DataGatewayController.QueryRequest("diagnostics." + view, Columns: new[] { column }), default));
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => Execute(reader, $"SELECT {column} FROM public.{view}"))).SqlState);
        }
        await Execute(admin, "INSERT INTO public.employee_lunch VALUES (3,1,1,1,'2026-11-10T18:00Z',NULL,now(),1)");
        var openFilter = new DataGatewayController.Filter("ended_at", "eq", JsonSerializer.SerializeToElement<string?>(null));
        var open = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await gateway.Query(
            new DataGatewayController.QueryRequest("diagnostics.employee_lunch", Filters: new[] { openFilter }), default)).Value);
        Assert.Single(open.GetProperty("rows").EnumerateArray());
        Assert.Equal("3", open.GetProperty("rows")[0].GetProperty("id").GetString());
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, NpgsqlTransaction? tx = null)
    { await using var command = new NpgsqlCommand(sql, connection, tx); await command.ExecuteNonQueryAsync(); }
}

public sealed class GatewayPostgreSqlFactAttribute : FactAttribute
{
    public GatewayPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLOW_POSTGRES_TEST_CONNECTION")))
            Skip = "Requires an isolated PostgreSQL server via FLOW_POSTGRES_TEST_CONNECTION; Backend CI supplies it.";
    }
}

[CollectionDefinition("Gateway database roles", DisableParallelization = true)]
public sealed class GatewayDatabaseRolesCollection;
