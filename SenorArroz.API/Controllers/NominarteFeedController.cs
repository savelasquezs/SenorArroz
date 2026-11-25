using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth-sensitive")]
[Route("api/integrations/laboral/v1")]
public sealed class NominarteFeedController(
    ApplicationDbContext db, ITenantExecutionContext tenants, IConfiguration config) : ControllerBase
{
    private const int SnapshotLimit = 10000;

    [HttpGet("snapshots")]
    public Task<IActionResult> GetSnapshot(CancellationToken ct) => ReadFeed(null, SnapshotLimit, true, ct);

    [HttpGet("changes")]
    public Task<IActionResult> GetChanges([FromQuery] string? cursor, [FromQuery] int limit = 250,
        CancellationToken ct = default) => ReadFeed(cursor, Math.Clamp(limit, 1, 500), false, ct);

    private async Task<IActionResult> ReadFeed(string? cursor, int limit, bool snapshot, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var token = Request.Headers["X-Nominarte-Key"].ToString();
        if (token.Length is < 32 or > 160) return Unauthorized();
        var tenantId = TenantForKey(token);
        if (tenantId is null) return Unauthorized();

        using var scope = tenants.BeginTenantScope(tenantId.Value);
        var sourceId = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId.Value && t.IsActive && t.Status == TenantStatus.Active)
            .Select(t => (Guid?)t.PublicId).SingleOrDefaultAsync(ct);
        if (sourceId is null) return Unauthorized();
        var after = 0L;
        if (!snapshot && !TryReadCursor(cursor, token, sourceId.Value, out after))
            return Problem(statusCode: 400, detail: "Cursor inválido. Solicita un snapshot inicial.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SELECT COALESCE((SELECT position FROM public.nominarte_feed_head WHERE tenant_id = @tenant), 0),
                   transaction_timestamp()
            """;
        AddParameter(command, "tenant", tenantId.Value);
        long head;
        DateTime cut;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            head = reader.GetInt64(0);
            cut = reader.GetDateTime(1);
        }
        if (after > head) return Problem(statusCode: 409, detail: "El origen cambió. Vuelve a solicitar el snapshot.");

        command.CommandText = snapshot ? """
            SELECT 0::bigint, 'lunch', id, 'upsert', updated_at,
                   public.nominarte_source_payload('lunch', to_jsonb(l))
            FROM public.employee_lunch l WHERE tenant_id = @tenant
            UNION ALL
            SELECT 0::bigint, 'leave', id, 'upsert', updated_at,
                   public.nominarte_source_payload('leave', to_jsonb(e))
            FROM public.employee_leave_request e WHERE tenant_id = @tenant
            ORDER BY 2, 3 LIMIT @take
            """ : """
            SELECT position, source, source_id, operation, occurred_at, payload
            FROM public.nominarte_feed_entry
            WHERE tenant_id = @tenant AND position > @after AND position <= @head
            ORDER BY position LIMIT @take
            """;
        AddParameter(command, "take", limit + 1);
        if (!snapshot)
        {
            AddParameter(command, "after", after);
            AddParameter(command, "head", head);
        }
        var rows = new List<FeedItem>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                rows.Add(new FeedItem(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetString(3), reader.GetDateTime(4), reader.GetFieldValue<JsonElement>(5)));
        }
        if (snapshot && rows.Count > limit)
            return Problem(statusCode: 413, detail: "El snapshot excede el volumen admitido para la carga inicial.");
        var hasMore = rows.Count > limit;
        var items = hasMore ? rows.Take(limit).ToArray() : rows.ToArray();
        if (!snapshot && (items.Length == 0 ? after < head : items[0].Position != after + 1))
            return Problem(statusCode: 409, detail: "El historial del feed está incompleto. Solicita un snapshot.");
        var next = snapshot ? head : items.LastOrDefault()?.Position ?? after;
        await transaction.CommitAsync(ct);
        return Ok(new
        {
            schemaVersion = 1, sourceId, items, hasMore, head, nextPosition = next,
            nextCursor = EncodeCursor(next, token, sourceId.Value),
            coveredAt = hasMore ? (DateTime?)null : cut
        });
    }

    private int? TenantForKey(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        foreach (var connector in config.GetSection("Nominarte:Connectors").GetChildren())
        {
            var digest = connector["KeySha256"];
            var tenantId = connector.GetValue<int>("TenantId");
            if (tenantId <= 0 || digest?.Length != 64) continue;
            byte[] expected;
            try { expected = Convert.FromHexString(digest); }
            catch (FormatException) { continue; }
            if (CryptographicOperations.FixedTimeEquals(expected, hash)) return tenantId;
        }
        return null;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string EncodeCursor(long position, string token, Guid sourceId)
    {
        var prefix = position.ToString(CultureInfo.InvariantCulture);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes($"{sourceId:N}:{prefix}"));
        return prefix + "." + WebEncoders.Base64UrlEncode(signature);
    }

    private static bool TryReadCursor(string? input, string token, Guid sourceId, out long position)
    {
        position = 0;
        if (input is null || input.Length > 72) return false;
        var pieces = input.Split('.', 2);
        return pieces.Length == 2 && long.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out position) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(input),
                Encoding.UTF8.GetBytes(EncodeCursor(position, token, sourceId)));
    }

    private sealed record FeedItem(long Position, string Source, int SourceId, string Operation, DateTime OccurredAt,
        JsonElement Payload);
}
