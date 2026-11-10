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
/// Opt-in read-only structured diagnostic gateway.
/// Never accepts SQL supplied by a client; all identifiers come from an allowlisted view.
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
    private static readonly Regex Identifier = new(
        @"\A[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public sealed record Filter(string Column, string Op, JsonElement Value);
    public sealed record QueryRequest(string View, string[]? Columns = null,
        Filter[]? Filters = null, string? OrderBy = null, bool Descending = false, int MaxRows = 100);

    private string[] AllowedViews => configuration.GetSection("DataGateway:AllowedViews")
        .Get<string[]>()?.Where(v => v.StartsWith("diagnostics.", StringComparison.Ordinal)
            && v.Split('.').Length == 2 && v.Split('.').All(IsIdentifier))
        .Distinct(StringComparer.Ordinal).ToArray() ?? [];

    private bool IsReady(out string connectionString)
    {
        connectionString = configuration.GetConnectionString("DataGatewayReader") ?? "";
        return configuration.GetValue<bool>("DataGateway:Enabled")
            && !string.IsNullOrWhiteSpace(connectionString)
            && tenant.HasTenant && tenant.TenantId > 0
            && AllowedViews.Length > 0;
    }

    private static bool IsIdentifier(string? value) =>
        value is { Length: > 0 and <= 63 } && Identifier.IsMatch(value);
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string QuoteView(string view) => string.Join(".", view.Split('.').Select(Quote));

    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> OpenAsync(
        string connectionString, CancellationToken ct)
    {
        var options = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MaxPoolSize = 3,
            Timeout = 3,
            CommandTimeout = 4,
            NoResetOnClose = false
        };
        var connection = new NpgsqlConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using var init = new NpgsqlCommand("""
                SET TRANSACTION READ ONLY;
                SELECT set_config('app.current_tenant_id', @tenant, true),
                       set_config('app.system_scope', 'off', true);
                SET LOCAL statement_timeout = '3000ms';
                """, connection, transaction);
            init.Parameters.AddWithValue("tenant", tenant.TenantId.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            await init.ExecuteNonQueryAsync(ct);
            return (connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<HashSet<string>> ColumnsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string view, CancellationToken ct)
    {
        const string sql = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'diagnostics' AND table_name = @view
            ORDER BY ordinal_position
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("view", view.Split('.')[1]);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    [HttpGet("schema")]
    public async Task<IActionResult> Schema(CancellationToken ct)
    {
        if (!IsReady(out var connectionString)) return NotFound();
        if (!await Concurrency.WaitAsync(0, ct)) return StatusCode(429);
        try
        {
            var (connection, tx) = await OpenAsync(connectionString, ct);
            await using (connection)
            await using (tx)
            {
                var result = new List<object>();
                foreach (var view in AllowedViews)
                {
                    var columns = await ColumnsAsync(connection, tx, view, ct);
                    result.Add(new { view, columns = columns.OrderBy(c => c).ToArray() });
                }
                await tx.CommitAsync(ct);
                return Ok(new { views = result });
            }
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "DataGateway schema error trace={Trace}", HttpContext.TraceIdentifier);
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
        if (!IsReady(out var connectionString)) return NotFound();
        if (!AllowedViews.Contains(request.View, StringComparer.Ordinal))
            return BadRequest(new { error = "View is not allowed" });
        if (request.MaxRows is < 1 or > 500 || request.Columns?.Length > 30 || request.Filters?.Length > 20)
            return BadRequest(new { error = "Request limits exceeded" });
        if (!await Concurrency.WaitAsync(0, ct)) return StatusCode(429);
        try
        {
            var (connection, tx) = await OpenAsync(connectionString, ct);
            await using (connection)
            await using (tx)
            {
                var allowedColumns = await ColumnsAsync(connection, tx, request.View, ct);
                var selected = request.Columns is { Length: > 0 }
                    ? request.Columns : allowedColumns.OrderBy(c => c).Take(30).ToArray();
                if (selected.Length == 0 || selected.Any(c => !allowedColumns.Contains(c))
                    || (request.OrderBy is not null && !allowedColumns.Contains(request.OrderBy))
                    || (request.Filters?.Any(f => !allowedColumns.Contains(f.Column)
                        || f.Op is not ("eq" or "ne" or "gt" or "gte" or "lt" or "lte")
                        || f.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                            or JsonValueKind.Undefined) ?? false))
                    return BadRequest(new { error = "Invalid columns or filters" });

                var sql = "SELECT " + string.Join(", ", selected.Select(Quote))
                    + " FROM " + QuoteView(request.View);
                await using var cmd = new NpgsqlCommand { Connection = connection, Transaction = tx };
                var predicates = new List<string>();
                var ops = new Dictionary<string, string>
                {
                    ["eq"] = "=", ["ne"] = "<>", ["gt"] = ">", ["gte"] = ">=",
                    ["lt"] = "<", ["lte"] = "<="
                };
                foreach (var (filter, index) in (request.Filters ?? []).Select((f, i) => (f, i)))
                {
                    var param = "p" + index;
                    if (filter.Value.ValueKind == JsonValueKind.Null)
                    {
                        if (filter.Op is not ("eq" or "ne"))
                            return BadRequest(new { error = "Invalid null comparison" });
                        predicates.Add(Quote(filter.Column) + (filter.Op == "eq" ? " IS NULL" : " IS NOT NULL"));
                        continue;
                    }
                    predicates.Add(Quote(filter.Column) + " " + ops[filter.Op] + " @" + param);
                    cmd.Parameters.Add(filter.Value.ValueKind switch
                    {
                        JsonValueKind.String => new NpgsqlParameter(param, NpgsqlDbType.Text)
                            { Value = filter.Value.GetString()! },
                        JsonValueKind.Number when filter.Value.TryGetInt64(out var integer) =>
                            new NpgsqlParameter(param, NpgsqlDbType.Bigint) { Value = integer },
                        JsonValueKind.Number => new NpgsqlParameter(param, NpgsqlDbType.Double)
                            { Value = filter.Value.GetDouble() },
                        JsonValueKind.True => new NpgsqlParameter(param, NpgsqlDbType.Boolean) { Value = true },
                        JsonValueKind.False => new NpgsqlParameter(param, NpgsqlDbType.Boolean) { Value = false },
                        _ => throw new ArgumentException("Unsupported filter")
                    });
                }
                if (predicates.Count > 0) sql += " WHERE " + string.Join(" AND ", predicates);
                if (request.OrderBy is not null)
                    sql += " ORDER BY " + Quote(request.OrderBy) + (request.Descending ? " DESC" : " ASC");
                sql += " LIMIT " + (request.MaxRows + 1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                cmd.CommandText = explain ? "EXPLAIN (FORMAT JSON) " + sql : sql;
                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                var rows = new List<Dictionary<string, object?>>();
                while (rows.Count <= request.MaxRows && await reader.ReadAsync(ct))
                {
                    var row = new Dictionary<string, object?>();
                    for (var i = 0; i < cols.Length; i++)
                    {
                        var value = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                        var serialized = value is null ? null : value is string s ? s : JsonSerializer.Serialize(value);
                        row[cols[i]] = serialized is { Length: > 2048 }
                            ? serialized[..2048] + "…" : serialized;
                    }
                    rows.Add(row);
                }
                var truncated = rows.Count > request.MaxRows;
                if (truncated) rows.RemoveAt(rows.Count - 1);
                await reader.CloseAsync();
                await tx.CommitAsync(ct);
                logger.LogInformation("DataGateway read tenant={Tenant} view={View} explain={Explain} rows={Count} trace={Trace}",
                    tenant.TenantId, request.View, explain, rows.Count, HttpContext.TraceIdentifier);
                return Ok(new { columns = cols, rows, truncated });
            }
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "DataGateway read error tenant={Tenant} trace={Trace}",
                tenant.TenantId, HttpContext.TraceIdentifier);
            return StatusCode(503, new { error = "Database query failed" });
        }
        finally { Concurrency.Release(); }
    }
}
