using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Controllers;

/// <summary>
/// Opt-in diagnostic gateway. Uses a SEPARATE, non-owner PostgreSQL reader credential.
/// Do not enable until that role's grants/RLS have been audited.
/// </summary>
[ApiController]
[Route("api/internal/data-gateway")]
[Authorize(Roles = "Superadmin")]
public sealed class DataGatewayController(
    IConfiguration configuration,
    ICurrentTenant tenant,
    ILogger<DataGatewayController> logger) : ControllerBase
{
    private static readonly SemaphoreSlim Concurrency = new(2, 2);
    private static readonly Regex ReadOnlySelect = new(
        @"\A\s*SELECT\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public sealed record QueryRequest(string Sql, Dictionary<string, JsonElement>? Parameters = null, int MaxRows = 100);

    private bool IsReady(out string connectionString)
    {
        connectionString = configuration.GetConnectionString("DataGatewayReader") ?? "";
        return configuration.GetValue<bool>("DataGateway:Enabled")
            && !string.IsNullOrWhiteSpace(connectionString)
            && tenant.HasTenant && tenant.TenantId > 0;
    }

    private static bool IsAllowed(string? sql) =>
        !string.IsNullOrWhiteSpace(sql)
        && sql.Length <= 8_000
        && ReadOnlySelect.IsMatch(sql)
        // A single SELECT, no SQL comments or batch statements. DB permissions are the real security boundary.
        && !sql.Contains(';')
        && !sql.Contains("--", StringComparison.Ordinal)
        && !sql.Contains("/*", StringComparison.Ordinal)
        && !sql.Contains("*/", StringComparison.Ordinal);

    private async Task<(NpgsqlConnection, NpgsqlTransaction)> OpenReadOnlyAsync(
        string connectionString, CancellationToken ct)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MaxPoolSize = 3,
            Timeout = 3,
            CommandTimeout = 4,
            NoResetOnClose = false
        };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using var init = new NpgsqlCommand(
                "SET TRANSACTION READ ONLY; SELECT set_config('app.current_tenant_id', @tenant, true), set_config('app.system_scope', 'off', true); SET LOCAL statement_timeout = '3000ms';",
                connection, tx);
            init.Parameters.AddWithValue("tenant", tenant.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await init.ExecuteNonQueryAsync(ct);
            return (connection, tx);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    [HttpGet("schema")]
    public async Task<IActionResult> Schema(CancellationToken ct)
    {
        if (!IsReady(out var cs)) return NotFound();
        if (!await Concurrency.WaitAsync(0, ct)) return StatusCode(429);
        try
        {
            await using var connection = new NpgsqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using var setup = new NpgsqlCommand(
                "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '3000ms';", connection, tx);
            await setup.ExecuteNonQueryAsync(ct);
            const string sql = """
                SELECT c.table_schema, c.table_name, c.column_name, c.data_type
                FROM information_schema.columns c
                WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND has_table_privilege(format('%I.%I', c.table_schema, c.table_name), 'SELECT')
                ORDER BY c.table_schema, c.table_name, c.ordinal_position
                LIMIT 1000
                """;
            await using var command = new NpgsqlCommand(sql, connection, tx);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var columns = new List<object>();
            while (await reader.ReadAsync(ct))
                columns.Add(new { schema = reader.GetString(0), table = reader.GetString(1),
                    column = reader.GetString(2), type = reader.GetString(3) });
            await reader.CloseAsync();
            await tx.CommitAsync(ct);
            return Ok(new { columns });
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "Data gateway schema failed trace={Trace}", HttpContext.TraceIdentifier);
            return StatusCode(503, new { error = "Database query failed" });
        }
        finally { Concurrency.Release(); }
    }

    [HttpPost("query")]
    public Task<IActionResult> Query([FromBody] QueryRequest request, CancellationToken ct)
        => Run(request, false, ct);

    [HttpPost("explain")]
    public Task<IActionResult> Explain([FromBody] QueryRequest request, CancellationToken ct)
        => Run(request, true, ct);

    private async Task<IActionResult> Run(QueryRequest request, bool explain, CancellationToken ct)
    {
        if (!IsReady(out var cs)) return NotFound();
        if (!IsAllowed(request.Sql)) return BadRequest(new { error = "Only one SELECT statement is accepted" });
        if (request.MaxRows is < 1 or > 500) return BadRequest(new { error = "MaxRows must be 1..500" });
        if (request.Parameters?.Count > 30) return BadRequest(new { error = "Too many parameters" });
        if (!await Concurrency.WaitAsync(0, ct)) return StatusCode(429);
        try
        {
            var (connection, tx) = await OpenReadOnlyAsync(cs, ct);
            await using (connection)
            await using (tx)
            {
                var sql = explain ? "EXPLAIN (FORMAT JSON) " + request.Sql : request.Sql;
                await using var command = new NpgsqlCommand(sql, connection, tx);
                foreach (var (name, value) in request.Parameters ?? new())
                {
                    if (!Regex.IsMatch(name, @"\A[a-zA-Z][a-zA-Z0-9_]{0,40}\z"))
                        return BadRequest(new { error = "Invalid parameter name" });
                    if (value.GetRawText().Length > 2048)
                        return BadRequest(new { error = "Parameter too large" });
                    var parameter = value.ValueKind switch
                    {
                        JsonValueKind.String => new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = value.GetString()! },
                        JsonValueKind.Number when value.TryGetInt64(out var number) =>
                            new NpgsqlParameter(name, NpgsqlDbType.Bigint) { Value = number },
                        JsonValueKind.Number => new NpgsqlParameter(name, NpgsqlDbType.Double) { Value = value.GetDouble() },
                        JsonValueKind.True => new NpgsqlParameter(name, NpgsqlDbType.Boolean) { Value = true },
                        JsonValueKind.False => new NpgsqlParameter(name, NpgsqlDbType.Boolean) { Value = false },
                        JsonValueKind.Null => new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = DBNull.Value },
                        _ => throw new ArgumentException("Only scalar parameters are supported")
                    };
                    command.Parameters.Add(parameter);
                }
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                var rows = new List<Dictionary<string, object?>>();
                while (rows.Count < (explain ? 1 : request.MaxRows) && await reader.ReadAsync(ct))
                {
                    var row = new Dictionary<string, object?>();
                    for (var i = 0; i < columns.Length; i++)
                    {
                        if (await reader.IsDBNullAsync(i, ct)) { row[columns[i]] = null; continue; }
                        // Bound response size, even for large TEXT/JSON columns.
                        var value = reader.GetValue(i);
                        var rendered = value is string s ? s : JsonSerializer.Serialize(value);
                        row[columns[i]] = rendered.Length > 2048 ? rendered[..2048] + "…" : rendered;
                    }
                    rows.Add(row);
                }
                await reader.CloseAsync();
                await tx.CommitAsync(ct);
                logger.LogInformation("DataGateway query tenant={Tenant} explain={Explain} rows={Count} trace={Trace}",
                    tenant.TenantId, explain, rows.Count, HttpContext.TraceIdentifier);
                return Ok(new { columns, rows, truncated = rows.Count >= (explain ? 1 : request.MaxRows) });
            }
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Invalid query parameters" });
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "DataGateway failed tenant={Tenant} trace={Trace}",
                tenant.TenantId, HttpContext.TraceIdentifier);
            return StatusCode(503, new { error = "Database query failed" });
        }
        finally { Concurrency.Release(); }
    }
}
