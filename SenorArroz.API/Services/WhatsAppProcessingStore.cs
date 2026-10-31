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
        foreach (var envelope in WhatsAppWebhookEnvelope.Split(payload))
        {
            var encrypted = protector.Protect(envelope.Payload);
            var safePayload = WhatsAppFlowPayload.WithoutTokens(envelope.Payload);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO whatsapp_webhook_event
                    (tenant_id, event_type, raw_payload, processed, processing_state, deduplication_key,
                     partition_key, protected_payload, next_attempt_at, created_at, updated_at)
                VALUES ({tenantId}, 'webhook', {safePayload}::jsonb, false, 'pending', {envelope.Key},
                    {envelope.Partition}, {encrypted}, {clock.UtcNow}, {clock.UtcNow}, {clock.UtcNow})
                ON CONFLICT (tenant_id, deduplication_key) WHERE deduplication_key IS NOT NULL DO NOTHING
                """, ct);
        }
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await WhatsAppDatabaseSignal.NotifyAsync(db.Database,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppProcessingStore>.Instance, ct);
    }

    public async Task<WhatsAppWebhookEvent?> ClaimAsync(CancellationToken ct)
    {
        using var system = execution.BeginSystemScope();
        var token = Guid.NewGuid();
        var now = clock.UtcNow;
        var leaseUntil = now.AddSeconds(options.Value.LeaseSeconds);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE whatsapp_webhook_event SET processing_state = 'review', lease_token = NULL,
                lease_until = NULL, processing_error = 'attempts_exhausted', updated_at = {now}
            WHERE processing_state IN ('pending', 'processing') AND (processing_attempts >= {options.Value.MaxAttempts}
                OR processed = true)
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
                .SetProperty(x => x.ProcessingState, x => succeeded ? "completed" : x.Processed
                    || job.ProcessingAttempts >= options.Value.MaxAttempts ? "review" : "pending")
                .SetProperty(x => x.ProcessingError, succeeded ? null : "processing_failed_requires_review_if_committed")
                .SetProperty(x => x.ProtectedPayload, x => succeeded ? null : x.ProtectedPayload)
                .SetProperty(x => x.NextAttemptAt, retryAt)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct) == 1;
    }
}
