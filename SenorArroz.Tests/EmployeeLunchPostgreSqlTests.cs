using Npgsql;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class EmployeeLunchPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task LunchAndAssignmentAreMutuallyExclusiveIncludingSerializableAcceptanceAndRls()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await using var admin = new NpgsqlConnection(postgres.GetConnectionString());
        await admin.OpenAsync();
        await Sql(admin, """
            CREATE SCHEMA app;
            CREATE FUNCTION app.current_tenant_id() RETURNS integer LANGUAGE sql STABLE
              AS $$ SELECT nullif(current_setting('app.current_tenant_id', true), '')::integer $$;
            CREATE FUNCTION app.is_system_scope() RETURNS boolean LANGUAGE sql STABLE AS $$ SELECT false $$;
            CREATE TABLE tenant(id integer PRIMARY KEY);
            CREATE TABLE branch(id integer PRIMARY KEY, tenant_id integer NOT NULL, UNIQUE(tenant_id,id));
            CREATE TABLE "user"(id integer PRIMARY KEY, tenant_id integer NOT NULL, updated_at timestamptz DEFAULT now(), UNIQUE(tenant_id,id));
            CREATE TABLE "order"(id integer PRIMARY KEY, tenant_id integer NOT NULL, delivery_man_id integer, status text);
            INSERT INTO tenant VALUES(1),(2);
            INSERT INTO branch VALUES(1,1),(2,2);
            INSERT INTO "user"(id,tenant_id) VALUES(1,1),(2,2);
            INSERT INTO "order" VALUES(1,1,NULL,'ready');
            """);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SenorArroz.Infrastructure/Scripts/add_employee_lunch.sql")))
            directory = directory.Parent;
        var script = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "SenorArroz.Infrastructure/Scripts/add_employee_lunch.sql"));
        await Sql(admin, script); await Sql(admin, script);
        await Sql(admin, """
            CREATE ROLE lunch_runtime LOGIN PASSWORD 'test-only-password' NOBYPASSRLS NOSUPERUSER;
            GRANT USAGE ON SCHEMA public,app TO lunch_runtime;
            GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO lunch_runtime;
            GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO lunch_runtime;
            """);
        var runtime = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Username = "lunch_runtime", Password = "test-only-password" }.ConnectionString;
        async Task<NpgsqlConnection> Connect(int tenant = 1)
        {
            var connection = new NpgsqlConnection(runtime); await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tenant, false)", connection);
            cmd.Parameters.AddWithValue("tenant", tenant.ToString()); await cmd.ExecuteNonQueryAsync();
            return connection;
        }
        const string start = "INSERT INTO employee_lunch(tenant_id,branch_id,user_id,started_at,started_by_user_id) VALUES(1,1,1,now(),1)";
        await using var lunch = await Connect(); await using var assignment = await Connect();
        await Sql(lunch, "BEGIN"); await Sql(lunch, start);
        await Sql(assignment, "BEGIN ISOLATION LEVEL SERIALIZABLE");
        var assigning = Sql(assignment, """UPDATE "order" SET delivery_man_id=1,status='on_the_way' WHERE id=1""");
        await WaitForLock(admin, assignment.ProcessID);
        await Sql(lunch, "COMMIT");
        var conflict = await Assert.ThrowsAsync<PostgresException>(() => assigning);
        Assert.Equal("40001", conflict.SqlState);
        await Sql(assignment, "ROLLBACK");
        Assert.Equal(0L, await Scalar(assignment, """SELECT count(*) FROM "order" WHERE delivery_man_id=1"""));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Sql(lunch, start));
        Assert.Equal("employee_lunch_overlap", duplicate.ConstraintName);

        await using (var otherTenant = await Connect(2))
        {
            Assert.Equal(0L, await Scalar(otherTenant, "SELECT count(*) FROM employee_lunch"));
            await Assert.ThrowsAsync<PostgresException>(() => Sql(otherTenant, start));
            var foreignActor = await Assert.ThrowsAsync<PostgresException>(() => Sql(otherTenant,
                "INSERT INTO employee_lunch(tenant_id,branch_id,user_id,started_at,started_by_user_id) VALUES(2,2,2,now(),1)"));
            Assert.Equal("23503", foreignActor.SqlState);
        }
        await Sql(lunch, "UPDATE employee_lunch SET ended_at=clock_timestamp(),ended_by_user_id=1");
        await Sql(assignment, "BEGIN");
        await Sql(assignment, """UPDATE "order" SET delivery_man_id=1,status='on_the_way' WHERE id=1""");
        var starting = Sql(lunch, start);
        await WaitForLock(admin, lunch.ProcessID);
        await Sql(assignment, "COMMIT");
        var pending = await Assert.ThrowsAsync<PostgresException>(() => starting);
        Assert.Equal("employee_lunch_pending_orders", pending.ConstraintName);
        Assert.Equal(0L, await Scalar(lunch, "SELECT count(*) FROM employee_lunch WHERE ended_at IS NULL"));
        Assert.Equal(1L, await Scalar(lunch, """SELECT count(*) FROM "order" WHERE delivery_man_id=1"""));
    }

    private static async Task Sql(NpgsqlConnection c, string sql) =>
        await new NpgsqlCommand(sql, c) { CommandTimeout = 30 }.ExecuteNonQueryAsync();
    private static async Task<long> Scalar(NpgsqlConnection c, string sql) =>
        (long)(await new NpgsqlCommand(sql, c).ExecuteScalarAsync())!;
    private static async Task WaitForLock(NpgsqlConnection admin, int pid)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await Scalar(admin, $"SELECT count(*) FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock'") == 1) return;
            await Task.Delay(25);
        }
        Assert.Fail("La segunda operación no esperó el bloqueo del empleado.");
    }
}
