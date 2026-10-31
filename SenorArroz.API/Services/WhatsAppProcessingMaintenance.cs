using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed class WhatsAppProcessingMaintenance(IServiceScopeFactory scopes,
    IOptions<WhatsAppProcessingOptions> options, ILogger<WhatsAppProcessingMaintenance> logger) : BackgroundService
{
    private readonly Meter _meter = new("SenorArroz.WhatsApp.Queue");
    private QueueSnapshot _snapshot = new(0, 0, 0, 0, 0, 0);

    public sealed record QueueSnapshot(long Pending, long Processing, long Review, long Retrying,
        double OldestPendingSeconds, long RealtimeEvents);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _meter.CreateObservableGauge("whatsapp.queue.pending", () => Volatile.Read(ref _snapshot).Pending);
        _meter.CreateObservableGauge("whatsapp.queue.processing", () => Volatile.Read(ref _snapshot).Processing);
        _meter.CreateObservableGauge("whatsapp.queue.review", () => Volatile.Read(ref _snapshot).Review);
        _meter.CreateObservableGauge("whatsapp.queue.retrying", () => Volatile.Read(ref _snapshot).Retrying);
        _meter.CreateObservableGauge("whatsapp.queue.oldest_pending", () => Volatile.Read(ref _snapshot).OldestPendingSeconds, "s");
        _meter.CreateObservableGauge("whatsapp.realtime.retained", () => Volatile.Read(ref _snapshot).RealtimeEvents);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.MetricsIntervalSeconds));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var system = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().BeginSystemScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                var snapshot = await ReadAsync(db, clock.UtcNow, ct);
                Volatile.Write(ref _snapshot, snapshot);
                logger.LogInformation("WhatsApp queue pending={Pending} processing={Processing} review={Review} retrying={Retrying} oldestSeconds={OldestSeconds} realtime={Realtime}",
                    snapshot.Pending, snapshot.Processing, snapshot.Review, snapshot.Retrying,
                    Math.Round(snapshot.OldestPendingSeconds, 1), snapshot.RealtimeEvents);
                await PruneAsync(db, clock.UtcNow, options.Value, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("WhatsApp queue maintenance failed. Category={Category}", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public static async Task<QueueSnapshot> ReadAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
    {
        var counts = await db.WhatsAppWebhookEvents.AsNoTracking()
            .Where(x => x.ProcessingState == "pending" || x.ProcessingState == "processing" || x.ProcessingState == "review")
            .GroupBy(x => 1).Select(g => new
            {
                Pending = g.LongCount(x => x.ProcessingState == "pending"),
                Processing = g.LongCount(x => x.ProcessingState == "processing"),
                Review = g.LongCount(x => x.ProcessingState == "review"),
                Retrying = g.LongCount(x => x.ProcessingState == "pending" && x.ProcessingAttempts > 0),
                Oldest = g.Where(x => x.ProcessingState == "pending").Min(x => (DateTime?)x.CreatedAt)
            }).SingleOrDefaultAsync(ct);
        return new(counts?.Pending ?? 0, counts?.Processing ?? 0, counts?.Review ?? 0, counts?.Retrying ?? 0,
            counts?.Oldest is DateTime oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0,
            await db.WhatsAppRealtimeEvents.LongCountAsync(ct));
    }

    public static async Task PruneAsync(ApplicationDbContext db, DateTime now, WhatsAppProcessingOptions options, CancellationToken ct)
    {
        var realtimeBefore = now.AddDays(-options.RealtimeRetentionDays);
        var payloadBefore = now.AddDays(-options.PayloadRetentionDays);
        const string empty = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH expired AS (
                SELECT id FROM whatsapp_realtime_event WHERE created_at < {realtimeBefore}
                ORDER BY created_at, id FOR UPDATE SKIP LOCKED LIMIT {options.CleanupBatchSize})
            DELETE FROM whatsapp_realtime_event e USING expired WHERE e.id = expired.id
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH expired AS (
                SELECT id FROM whatsapp_webhook_event
                WHERE processing_state IN ('completed', 'review') AND updated_at < {payloadBefore}
                    AND (raw_payload <> {empty}::jsonb OR protected_payload IS NOT NULL OR protected_result IS NOT NULL)
                ORDER BY updated_at, id FOR UPDATE SKIP LOCKED LIMIT {options.CleanupBatchSize})
            UPDATE whatsapp_webhook_event e SET raw_payload = {empty}::jsonb, protected_payload = NULL, protected_result = NULL
            FROM expired WHERE e.id = expired.id
            """, ct);
    }

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
