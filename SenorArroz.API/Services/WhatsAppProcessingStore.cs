using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed class WhatsAppProcessingStore(ApplicationDbContext db, ITenantExecutionContext execution,
    IIntegrationSecretProtector protector, IOptions<WhatsAppProcessingOptions> options, IClock clock,
    ILogger<WhatsAppProcessingStore>? logger = null)
{
    public async Task EnqueueAsync(int tenantId, string payload, CancellationToken ct)
    {
        using var tenant = execution.BeginTenantScope(tenantId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var envelope in WhatsAppWebhookEnvelope.Split(payload).OrderBy(x => x.PhoneNumberId, StringComparer.Ordinal))
        {
            var known = await db.WhatsAppConversations.AsNoTracking()
                .Where(x => (envelope.PhoneNumber != null && x.PhoneNumber == envelope.PhoneNumber
                    || envelope.UserId != null && x.WhatsAppUserId == envelope.UserId)
                    && (x.ChannelSettingId != null && db.WhatsAppChannelSettings.Any(c => c.Id == x.ChannelSettingId && c.PhoneNumberId == envelope.PhoneNumberId)
                        || x.ChannelSettingId == null && db.WhatsAppBranchSettings.Any(c => c.BranchId == x.BranchId && c.PhoneNumberId == envelope.PhoneNumberId)))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.PhoneNumber, x.WhatsAppUserId, x.AttentionModeUpdatedAt }).Take(2).ToListAsync(ct);
            var established = known.FirstOrDefault(x => envelope.UserId != null && x.WhatsAppUserId == envelope.UserId)
                ?? known.FirstOrDefault(x => x.PhoneNumber == envelope.PhoneNumber
                    && (envelope.UserId == null || string.IsNullOrEmpty(x.WhatsAppUserId) || x.WhatsAppUserId == envelope.UserId));
            int? conversationId = established?.Id;
            DateTime? attentionVersion = established?.AttentionModeUpdatedAt;
            var aliases = envelope.Aliases.Concat(known.SelectMany(x =>
                WhatsAppWebhookEnvelope.AliasesFor(envelope.PhoneNumberId, x.PhoneNumber, x.WhatsAppUserId))).Distinct().ToArray();
            var resolved = await ResolvePartitionAsync(tenantId, envelope.PhoneNumberId, aliases, conversationId, envelope.Partition, ct);
            var partition = resolved.Key;
            aliases = resolved.Aliases;
            var encrypted = protector.Protect(envelope.Payload);
            var safePayload = WhatsAppFlowPayload.WithoutTokens(envelope.Payload);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO whatsapp_webhook_event
                    (tenant_id, event_type, raw_payload, processed, processing_state, deduplication_key,
                     partition_key, partition_aliases, protected_payload, conversation_id, attention_version, next_attempt_at, created_at, updated_at)
                VALUES ({tenantId}, 'webhook', {safePayload}::jsonb, false, 'pending', {envelope.Key},
                    {partition}, {aliases}, {encrypted}, {conversationId}, {attentionVersion}, {clock.UtcNow}, {clock.UtcNow}, {clock.UtcNow})
                ON CONFLICT (tenant_id, deduplication_key) WHERE deduplication_key IS NOT NULL DO UPDATE
                SET partition_key = EXCLUDED.partition_key,
                    partition_aliases = ARRAY(SELECT DISTINCT unnest(whatsapp_webhook_event.partition_aliases || EXCLUDED.partition_aliases))
                """, ct);
        }
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await WhatsAppDatabaseSignal.NotifyAsync(db.Database,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppProcessingStore>.Instance, ct);
    }

    public async Task<(string Key, string[] Aliases)> ResolvePartitionAsync(int tenantId, string? phoneNumberId,
        string[] aliases, int? conversationId, string fallback, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Partition resolution requires an ingress transaction.");
        using var tenant = execution.BeginTenantScope(tenantId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({tenantId}, hashtext({phoneNumberId ?? fallback}))", ct);
        var earlier = await db.WhatsAppWebhookEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && (conversationId.HasValue && x.ConversationId == conversationId
                || x.PartitionAliases.Any(a => aliases.Contains(a))))
            .OrderBy(x => x.Id).Select(x => new { x.PartitionKey, x.PartitionAliases }).Take(200).ToListAsync(ct);
        var partition = earlier.FirstOrDefault()?.PartitionKey ?? fallback;
        aliases = aliases.Concat(earlier.SelectMany(x => x.PartitionAliases)).Distinct().ToArray();
        var oldPartitions = earlier.Select(x => x.PartitionKey).Distinct().Where(x => x != partition).ToArray();
        if (oldPartitions.Length > 0)
            await db.WhatsAppWebhookEvents.Where(x => x.TenantId == tenantId && oldPartitions.Contains(x.PartitionKey)
                && (x.ProcessingState == "pending" || x.ProcessingState == "processing"))
                .ExecuteUpdateAsync(x => x.SetProperty(e => e.PartitionKey, partition), ct);
        return (partition, aliases);
    }

    public async Task<WhatsAppWebhookEvent?> ClaimAsync(CancellationToken ct)
    {
        using var system = execution.BeginSystemScope();
        var token = Guid.NewGuid();
        var now = clock.UtcNow;
        var leaseUntil = now.AddSeconds(options.Value.LeaseSeconds);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE whatsapp_webhook_event SET processing_state = 'completed', protected_payload = NULL,
                lease_token = NULL, lease_until = NULL, processing_error = NULL, updated_at = {now}
            WHERE event_type = 'manual_operation' AND processing_state = 'processing' AND processed = true
                AND protected_result IS NOT NULL AND lease_until < {now}
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE whatsapp_webhook_event SET processing_state = 'review', lease_token = NULL,
                lease_until = NULL, processing_error = 'attempts_exhausted', updated_at = {now}
            WHERE processing_state IN ('pending', 'processing') AND (processing_attempts >= {options.Value.MaxAttempts}
                OR processed = true OR event_type = 'manual_operation' AND processing_state = 'processing')
                AND (lease_until IS NULL OR lease_until < {now});
            """, ct);
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidate AS (
                SELECT w.id FROM whatsapp_webhook_event w
                WHERE w.processing_state IN ('pending', 'processing')
                    AND w.processing_attempts < {options.Value.MaxAttempts}
                    AND w.next_attempt_at <= {now} AND (w.lease_until IS NULL OR w.lease_until < {now})
                    AND NOT EXISTS (
                        SELECT 1 FROM whatsapp_webhook_event earlier
                        WHERE earlier.tenant_id = w.tenant_id AND earlier.partition_key = w.partition_key
                            AND earlier.id < w.id AND earlier.processing_state IN ('pending', 'processing'))
                ORDER BY w.id FOR UPDATE SKIP LOCKED LIMIT 1)
            UPDATE whatsapp_webhook_event w SET processing_state = 'processing', lease_token = {token},
                lease_until = {leaseUntil}, processing_attempts = processing_attempts + 1, updated_at = {now}
            FROM candidate WHERE w.id = candidate.id
            """, ct);
        return count == 0 ? null : await db.WhatsAppWebhookEvents.AsNoTracking()
            .SingleAsync(x => x.LeaseToken == token, ct);
    }

    public string ReadPayload(WhatsAppWebhookEvent job) => protector.Unprotect(job.ProtectedPayload
        ?? throw new InvalidOperationException("Missing protected webhook payload."));

    public async Task<bool> FinishAsync(WhatsAppWebhookEvent job, bool succeeded, CancellationToken ct)
    {
        using var tenant = execution.BeginTenantScope(job.TenantId);
        var retryAt = clock.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, job.ProcessingAttempts) * 5));
        return await db.WhatsAppWebhookEvents.Where(x => x.Id == job.Id && x.LeaseToken == job.LeaseToken
                && x.ProcessingState == "processing" && x.LeaseUntil > clock.UtcNow)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.ProcessingState, x => succeeded ? "completed" : x.EventType == "manual_operation" || x.Processed
                    || job.ProcessingAttempts >= options.Value.MaxAttempts ? "review" : "pending")
                .SetProperty(x => x.ProcessingError, succeeded ? null : "processing_failed_requires_review_if_committed")
                .SetProperty(x => x.ProtectedPayload, x => succeeded ? null : x.ProtectedPayload)
                .SetProperty(x => x.NextAttemptAt, retryAt)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct) == 1;
    }
}
