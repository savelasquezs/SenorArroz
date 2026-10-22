using Npgsql;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class EmployeeLeaveV2PostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task UpgradePreservesExistingDecisionsAndEnforcesFullDaysAndPrivateReviewOwnership()
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
        await Sql(admin, await Script("add_employee_leave_requests.sql"));
        await Sql(admin, """
            INSERT INTO employee_leave_request(tenant_id,public_id,request_key,user_id,branch_id,applicant_name,applicant_role,
                kind,starts_at,ends_at,justification,status,is_paid,decided_by_user_id,decided_at,decision_reason,support_status)
            VALUES(1,gen_random_uuid(),gen_random_uuid(),1,1,'Persona','Kitchen','medical_appointment',
                '2026-10-20T13:00:00Z','2026-10-20T14:00:00Z','Solicitud histórica','approved',true,1,now(),'Decisión histórica','verified');
            INSERT INTO employee_leave_attachment(tenant_id,request_id,upload_key,uploaded_by_user_id,file_name,content_type,storage_key,size)
            VALUES(1,1,gen_random_uuid(),1,'soporte.pdf','application/pdf','tenants/1/private',10);
            """);
        var script = await Script("improve_employee_leave_requests.sql");
        await Sql(admin, script); await Sql(admin, script);
        Assert.Equal(1L, await Scalar(admin, "SELECT count(*) FROM employee_leave_request WHERE policy_version=1 AND NOT is_all_day AND status='approved' AND is_paid AND decision_reason='Decisión histórica' AND support_status='verified'"));
        Assert.Equal(1L, await Scalar(admin, "SELECT count(*) FROM employee_leave_attachment WHERE evidence_code='additional' AND review_status='submitted' AND storage_key='tenants/1/private'"));
        Assert.Equal(2L, await Scalar(admin, "SELECT count(*) FROM pg_class WHERE relname IN ('employee_leave_request','employee_leave_attachment') AND relrowsecurity AND relforcerowsecurity"));
        Assert.Equal(8L, await Scalar(admin, "SELECT count(*) FROM pg_policies WHERE tablename IN ('employee_leave_request','employee_leave_attachment')"));
        var runtime = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Username = "senorarroz_runtime", Password = "test-only-password" }.ConnectionString;
        await using var user = new NpgsqlConnection(runtime); await user.OpenAsync();
        await Sql(user, "SELECT set_config('app.current_tenant_id','1',false)");
        await Reject(user, "UPDATE employee_leave_request SET is_all_day=true", "23514");
        await Reject(user, "UPDATE employee_leave_request SET policy_version=2", "23514");
        await Sql(user, "UPDATE employee_leave_request SET starts_at='2026-10-20T05:00:00Z',ends_at='2026-10-21T05:00:00Z',is_all_day=true,requested_is_paid=true,policy_version=2");
        await Reject(user, "UPDATE employee_leave_attachment SET review_status='verified'", "23514");
        await Reject(user, "UPDATE employee_leave_attachment SET review_status='verified',review_reason='Revisado',reviewed_at=now(),reviewed_by_user_id=2", "23503");
        await Sql(user, "UPDATE employee_leave_attachment SET review_status='verified',review_reason='Revisado',reviewed_at=now(),reviewed_by_user_id=1");
        await Sql(user, "SELECT set_config('app.current_tenant_id','2',false)");
        Assert.Equal(0L, await Scalar(user, "SELECT count(*) FROM employee_leave_request"));
        Assert.Equal(0L, await Scalar(user, "SELECT count(*) FROM employee_leave_attachment"));
        await Sql(user, "UPDATE employee_leave_attachment SET review_reason='No autorizado'");
        Assert.Equal(1L, await Scalar(admin, "SELECT count(*) FROM employee_leave_attachment WHERE review_reason='Revisado'"));
    }
    private static async Task<string> Script(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var relative = Path.Combine("SenorArroz.Infrastructure", "Scripts", name);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, relative))) directory = directory.Parent;
        Assert.NotNull(directory);
        return await File.ReadAllTextAsync(Path.Combine(directory!.FullName, relative));
    }
    private static async Task Sql(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); return (long)(await command.ExecuteScalarAsync())!; }
    private static async Task Reject(NpgsqlConnection connection, string sql, string code)
    { var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(connection, sql)); Assert.Equal(code, error.SqlState); }
}
