using Npgsql;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class EmployeeLeavePostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task ProductionScriptIsIdempotentAndEnforcesTenantOwnershipPrivateFilesAndDecisionReasons()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await using var admin = new NpgsqlConnection(postgres.GetConnectionString()); await admin.OpenAsync();
        await Sql(admin, """
            CREATE SCHEMA app;
            CREATE ROLE senorarroz_runtime LOGIN PASSWORD 'test-only-password' NOBYPASSRLS NOSUPERUSER;
            GRANT USAGE ON SCHEMA public,app TO senorarroz_runtime;
            CREATE FUNCTION app.current_tenant_id() RETURNS integer LANGUAGE sql STABLE
              AS $$ SELECT nullif(current_setting('app.current_tenant_id',true),'')::integer $$;
            CREATE FUNCTION app.is_system_scope() RETURNS boolean LANGUAGE sql STABLE AS $$ SELECT false $$;
            CREATE TABLE tenant(id integer PRIMARY KEY);
            CREATE TABLE branch(id integer PRIMARY KEY,tenant_id integer NOT NULL,UNIQUE(tenant_id,id));
            CREATE TABLE "user"(id integer PRIMARY KEY,tenant_id integer NOT NULL,role text NOT NULL,
              payroll_expense_id integer,delivery_app_enabled boolean NOT NULL DEFAULT false,
              active boolean NOT NULL DEFAULT false,UNIQUE(tenant_id,id));
            INSERT INTO tenant VALUES(1),(2);
            INSERT INTO branch VALUES(1,1),(2,2);
            INSERT INTO "user"(id,tenant_id,role) VALUES(1,1,'kitchen'),(2,2,'kitchen');
            """);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string relative = "SenorArroz.Infrastructure/Scripts/add_employee_leave_requests.sql";
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, relative))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, relative));
        await Sql(admin, script); await Sql(admin, script);
        Assert.Equal(2L, await Scalar(admin, "SELECT count(*) FROM public.\"user\" WHERE NOT active AND account_type='personal'"));
        Assert.Equal(8L, await Scalar(admin, "SELECT count(*) FROM pg_policies WHERE tablename IN ('employee_leave_request','employee_leave_attachment')"));
        Assert.Equal(2L, await Scalar(admin, "SELECT count(*) FROM pg_class WHERE relname IN ('employee_leave_request','employee_leave_attachment') AND relrowsecurity AND relforcerowsecurity"));
        var runtime = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            { Username = "senorarroz_runtime", Password = "test-only-password" }.ConnectionString;
        await using var user = new NpgsqlConnection(runtime); await user.OpenAsync();
        await Sql(user, "SELECT set_config('app.current_tenant_id','1',false)");
        const string insert = """
            INSERT INTO employee_leave_request(tenant_id,public_id,request_key,user_id,branch_id,applicant_name,applicant_role,kind,starts_at,ends_at,justification)
            VALUES(1,gen_random_uuid(),gen_random_uuid(),1,1,'Persona','Kitchen','other',now(),now()+interval '1 hour','Diligencia')
            """;
        await Sql(user, insert);
        Assert.Equal(1L, await Scalar(user, "SELECT count(*) FROM employee_leave_request"));
        await Reject(user, "UPDATE employee_leave_request SET status='denied',decision_reason=NULL", "23514");
        await Reject(user, "UPDATE employee_leave_request SET status='denied',decision_reason='   '", "23514");
        await Sql(user, "UPDATE employee_leave_request SET status='denied',decision_reason='No autorizado'");
        await Reject(user, "UPDATE employee_leave_request SET user_id=2", "23503");
        await Reject(user, "UPDATE employee_leave_request SET tenant_id=2", "42501");
        await Reject(user, "DELETE FROM employee_leave_request", "42501");
        await Sql(user, """
            INSERT INTO employee_leave_attachment(tenant_id,request_id,upload_key,uploaded_by_user_id,file_name,content_type,storage_key,size)
            VALUES(1,1,gen_random_uuid(),1,'soporte.pdf','application/pdf','tenants/1/private',10)
            """);
        await Reject(user, "UPDATE employee_leave_attachment SET uploaded_by_user_id=2", "23503");
        await Reject(user, "UPDATE employee_leave_attachment SET size=5242881", "23514");
        await Sql(user, "SELECT set_config('app.current_tenant_id','2',false)");
        Assert.Equal(0L, await Scalar(user, "SELECT count(*) FROM employee_leave_request"));
        Assert.Equal(0L, await Scalar(user, "SELECT count(*) FROM employee_leave_attachment"));
        await Reject(user, insert, "42501");
        await Sql(user, "DELETE FROM employee_leave_attachment");
        Assert.Equal(1L, await Scalar(admin, "SELECT count(*) FROM employee_leave_attachment"));
        await Sql(user, "SELECT set_config('app.current_tenant_id','1',false)");
        await Sql(user, "DELETE FROM employee_leave_attachment");
        Assert.Equal(0L, await Scalar(admin, "SELECT count(*) FROM employee_leave_attachment"));
    }
    private static async Task Sql(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); return (long)(await command.ExecuteScalarAsync())!; }
    private static async Task Reject(NpgsqlConnection connection, string sql, string code)
    { var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(connection, sql)); Assert.Equal(code, error.SqlState); }
}
