using Npgsql;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class RoutingOperationalSchemaTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task ScriptIsAdditiveIdempotentAndRejectsForeignSessionScope()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        async Task Execute(string sql) => await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
        await Execute("""
            CREATE TABLE delivery_work_session(tenant_id int NOT NULL,id int PRIMARY KEY,
                deliveryman_id int NOT NULL,branch_id int NOT NULL);
            CREATE TABLE delivery_route(id int PRIMARY KEY,tenant_id int NOT NULL,
                deliveryman_id int NOT NULL,branch_id int NOT NULL,status text NOT NULL);
            ALTER TABLE delivery_route ENABLE ROW LEVEL SECURITY;
            ALTER TABLE delivery_route FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_guard ON delivery_route USING (tenant_id = 1);
            INSERT INTO delivery_work_session VALUES(1,10,7,1),(2,20,8,2);
            INSERT INTO delivery_route VALUES(30,1,7,1,'in_progress');
            """);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SenorArroz.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = await File.ReadAllTextAsync(Path.Combine(directory.FullName,
            "SenorArroz.Infrastructure", "Scripts", "add_routing_operational_session.sql"));
        await Execute(script);
        await Execute(script);
        Assert.Equal("in_progress", (string)(await new NpgsqlCommand("SELECT status FROM delivery_route WHERE id=30", connection).ExecuteScalarAsync())!);
        Assert.Equal(1L, (long)(await new NpgsqlCommand("SELECT count(*) FROM delivery_route WHERE work_session_id IS NULL AND departed_at_utc IS NULL", connection).ExecuteScalarAsync())!);
        Assert.True((bool)(await new NpgsqlCommand("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='delivery_route'::regclass", connection).ExecuteScalarAsync())!);
        await Execute("UPDATE delivery_route SET work_session_id=10 WHERE id=30");
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => Execute("UPDATE delivery_route SET work_session_id=20 WHERE id=30"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        var malformed = await Assert.ThrowsAsync<PostgresException>(() => Execute("UPDATE delivery_route SET departed_at_utc=now(), departure_source=NULL WHERE id=30"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, malformed.SqlState);
        await Execute("UPDATE delivery_route SET departed_at_utc=now(), departure_source='gps' WHERE id=30");
        var retained = await Assert.ThrowsAsync<PostgresException>(() => Execute("DELETE FROM delivery_work_session WHERE id=10"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, retained.SqlState);
    }
}
