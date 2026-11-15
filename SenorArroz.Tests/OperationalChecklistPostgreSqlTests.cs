using Npgsql;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class OperationalChecklistPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    public async Task SchemaSeedRlsAndDailyStateAreIdempotent()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build(); await postgres.StartAsync();
        await using var admin = new NpgsqlConnection(postgres.GetConnectionString()); await admin.OpenAsync();
        await Sql(admin, """
            CREATE SCHEMA app;
            CREATE FUNCTION app.current_tenant_id() RETURNS integer LANGUAGE sql STABLE AS $$ SELECT nullif(current_setting('app.current_tenant_id',true),'')::integer $$;
            CREATE FUNCTION app.is_system_scope() RETURNS boolean LANGUAGE sql STABLE AS $$ SELECT false $$;
            CREATE TABLE tenant(id integer PRIMARY KEY,public_id uuid UNIQUE);
            CREATE TABLE branch(id integer PRIMARY KEY,tenant_id integer NOT NULL,UNIQUE(tenant_id,id));
            INSERT INTO tenant VALUES(1,'00000000-0000-0000-0000-000000000001'),(2,'00000000-0000-0000-0000-000000000002');
            INSERT INTO branch VALUES(1,1),(2,1),(3,2);
            CREATE ROLE senorarroz_runtime LOGIN PASSWORD 'test-only' NOBYPASSRLS NOSUPERUSER;
            GRANT USAGE ON SCHEMA public,app TO senorarroz_runtime;
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"SenorArroz.Infrastructure/Scripts/add_operational_checklist.sql"))) root=root.Parent;
        var script = await File.ReadAllTextAsync(Path.Combine(root!.FullName,"SenorArroz.Infrastructure/Scripts/add_operational_checklist.sql"));
        await Sql(admin, script); await Sql(admin, script);
        await Sql(admin, """
            INSERT INTO operational_reminder(tenant_id,branch_id,code,title,description,period,days_of_week,responsible,manual_path)
              VALUES(1,1,'TEST-1','Test','Test','morning',ARRAY[0,1,2,4,5,6],'cashier','/operacion/responsable-caja'),
                    (1,2,'TEST-2','Test','Test','evening',ARRAY[0,1,2,4,5,6],'admin','/operacion/responsable-caja');
            INSERT INTO operational_reminder_state(tenant_id,branch_id,reminder_id) SELECT tenant_id,branch_id,id FROM operational_reminder;
            """);
        Assert.Equal(2L, await Scalar(admin,"SELECT count(*) FROM operational_reminder"));
        Assert.Equal(2L, await Scalar(admin,"SELECT count(*) FROM operational_reminder_state"));
        await Sql(admin,"UPDATE operational_reminder SET title='Edited in POS' WHERE id=(SELECT min(id) FROM operational_reminder)");
        await Sql(admin,script);
        Assert.Equal(1L,await Scalar(admin,"SELECT count(*) FROM operational_reminder WHERE title='Edited in POS'"));
        var cs = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) {Username="senorarroz_runtime",Password="test-only"}.ConnectionString;
        await using var runtime = new NpgsqlConnection(cs); await runtime.OpenAsync();
        await Sql(runtime,"SELECT set_config('app.current_tenant_id','2',false)");
        Assert.Equal(0L,await Scalar(runtime,"SELECT count(*) FROM operational_reminder"));
        Assert.Equal(0L,await Scalar(runtime,"SELECT count(*) FROM operational_reminder_state"));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql(runtime,"INSERT INTO operational_reminder_state(tenant_id,branch_id,reminder_id) VALUES(1,1,999999)"));
        await Sql(runtime,"SELECT set_config('app.current_tenant_id','1',false)");
        Assert.Equal(2L,await Scalar(runtime,"SELECT count(*) FROM operational_reminder_state"));
        await Sql(runtime,"UPDATE operational_reminder_state SET operation_date='2026-11-15',is_checked=true,revision=revision+1");
        await Sql(runtime,"UPDATE operational_reminder_state SET operation_date='2026-11-16',is_checked=true,revision=revision+1");
        Assert.Equal(2L,await Scalar(runtime,"SELECT count(*) FROM operational_reminder_state"));
        Assert.Equal(0L,await Scalar(runtime,"SELECT count(*) FROM operational_reminder_state WHERE operation_date='2026-11-15'"));
    }
    private static Task Sql(NpgsqlConnection c,string sql)=>new NpgsqlCommand(sql,c).ExecuteNonQueryAsync();
    private static async Task<long> Scalar(NpgsqlConnection c,string sql)=>(long)(await new NpgsqlCommand(sql,c).ExecuteScalarAsync())!;
}
