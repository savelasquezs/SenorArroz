using Npgsql;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class EmployeeAccountMigrationPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task ConfirmedTerminalsAreExactTenantScopedIdempotentAndPreservePersonalAccounts()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await using var db = new NpgsqlConnection(postgres.GetConnectionString());
        await db.OpenAsync();
        await Sql(db, """
            CREATE TABLE tenant(id integer PRIMARY KEY,slug text NOT NULL);
            INSERT INTO tenant VALUES(1,'senor-arroz'),(2,'another-tenant');
            CREATE TABLE "user"(id integer PRIMARY KEY,tenant_id integer NOT NULL,branch_id integer NOT NULL,
                email text NOT NULL,role text NOT NULL DEFAULT 'kitchen',active boolean NOT NULL DEFAULT true,
                web_access_enabled boolean NOT NULL DEFAULT true,delivery_app_enabled boolean NOT NULL DEFAULT false,
                payroll_expense_id integer,account_type text NOT NULL DEFAULT 'personal',password_hash text DEFAULT 'unchanged',
                active_session_id text DEFAULT 'same-session');
            INSERT INTO "user"(id,tenant_id,branch_id,email) VALUES
                (5,1,1,'JUAN@JUAN.COM'),(10,1,2,'pao.villorina@hotmail.com'),
                (3,1,1,'personal@example.invalid'),(11,1,1,'inactive@example.invalid'),
                (20,2,3,'juan@juan.com');
            UPDATE "user" SET web_access_enabled=false,payroll_expense_id=53 WHERE id=3;
            UPDATE "user" SET active=false,web_access_enabled=false WHERE id=11;
            CREATE TABLE employee_lunch(tenant_id integer,user_id integer);
            CREATE TABLE employee_leave_request(tenant_id integer,user_id integer);
            INSERT INTO employee_lunch VALUES(1,3),(2,20);
            """);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string path = "SenorArroz.Infrastructure/Scripts/configure_senor_arroz_kitchen_terminals.sql";
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, path))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, path));
        await Sql(db, script);
        await Sql(db, script);
        Assert.Equal(2L, await Count(db, "SELECT count(*) FROM \"user\" WHERE tenant_id=1 AND account_type='terminal'"));
        Assert.Equal(1L, await Count(db, "SELECT count(*) FROM \"user\" WHERE id=3 AND account_type='personal' AND active AND NOT web_access_enabled AND payroll_expense_id=53"));
        Assert.Equal(1L, await Count(db, "SELECT count(*) FROM \"user\" WHERE id=11 AND NOT active AND NOT web_access_enabled AND account_type='personal'"));
        Assert.Equal(1L, await Count(db, "SELECT count(*) FROM \"user\" WHERE tenant_id=2 AND account_type='personal'"));
        Assert.Equal(5L, await Count(db, "SELECT count(*) FROM \"user\" WHERE password_hash='unchanged' AND active_session_id='same-session'"));

        await Sql(db, "UPDATE \"user\" SET account_type='personal' WHERE tenant_id=1; UPDATE \"user\" SET email='wrong@example.invalid' WHERE id=10");
        await Reject(db, script);
        Assert.Equal(0L, await Count(db, "SELECT count(*) FROM \"user\" WHERE account_type='terminal'"));
        await Sql(db, "UPDATE \"user\" SET email='pao.villorina@hotmail.com',payroll_expense_id=99 WHERE id=10");
        await Reject(db, script);
        await Sql(db, "UPDATE \"user\" SET payroll_expense_id=NULL WHERE id=10; INSERT INTO employee_lunch VALUES(1,5)");
        await Reject(db, script);
        await Sql(db, "DELETE FROM employee_lunch WHERE user_id=5; INSERT INTO employee_leave_request VALUES(1,10)");
        await Reject(db, script);
        await Sql(db, "DELETE FROM employee_leave_request; UPDATE tenant SET slug='wrong-tenant' WHERE id=1");
        await Reject(db, script);
        Assert.Equal(0L, await Count(db, "SELECT count(*) FROM \"user\" WHERE account_type='terminal'"));
    }

    private static async Task Sql(NpgsqlConnection db, string sql)
    { await using var command = new NpgsqlCommand(sql, db); await command.ExecuteNonQueryAsync(); }
    private static async Task<long> Count(NpgsqlConnection db, string sql)
    { await using var command = new NpgsqlCommand(sql, db); return (long)(await command.ExecuteScalarAsync())!; }
    private static async Task Reject(NpgsqlConnection db, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(db, sql));
        Assert.Equal("P0001", error.SqlState);
        await Sql(db, "ROLLBACK");
    }
}
