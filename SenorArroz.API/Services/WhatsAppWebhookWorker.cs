using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed class WhatsAppWebhookWorker(IServiceScopeFactory scopes,
    IOptions<WhatsAppProcessingOptions> options, ILogger<WhatsAppWebhookWorker> logger,
    WhatsAppDatabaseSignal? signal = null) : BackgroundService
{
    private static readonly Meter Meter = new("SenorArroz.WhatsApp.Processing");
    private static readonly Counter<long> Completed = Meter.CreateCounter<long>("whatsapp.webhooks.completed");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("whatsapp.webhooks.failed");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("whatsapp.webhooks.duration", "ms");
    private static readonly Histogram<double> QueueAge = Meter.CreateHistogram<double>("whatsapp.webhooks.queue_age", "s");

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        Enumerable.Range(0, options.Value.Concurrency).Select(_ => ConsumeAsync(stoppingToken)));

    private async Task ConsumeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var version = signal?.Version ?? 0;
            try
            {
                if (await ProcessOneAsync(ct)) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "WhatsApp durable consumer cycle failed."); }
            try
            {
                if (signal is not null) await signal.WaitAsync(version, ct);
                else await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<WhatsAppProcessingStore>();
        var job = await store.ClaimAsync(ct);
        if (job is null) return false;
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().BeginTenantScope(job.TenantId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.WorkTimeoutSeconds));
        QueueAge.Record(Math.Max(0, (DateTime.UtcNow - job.CreatedAt).TotalSeconds));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var succeeded = false;
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (job.EventType == "manual_operation")
                await scope.ServiceProvider.GetRequiredService<WhatsAppManualWorkQueue>()
                    .ExecuteAsync(job, store.ReadPayload(job), timeout.Token);
            else
            {
                var tracked = await db.WhatsAppWebhookEvents.SingleAsync(x => x.Id == job.Id, timeout.Token);
                await scope.ServiceProvider.GetRequiredService<WhatsAppWebhookProcessor>()
                    .ProcessAsync(tracked, store.ReadPayload(job), timeout.Token);
                job.WhatsAppMessageId = tracked.WhatsAppMessageId;
            }
            succeeded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Failed.Add(1);
            logger.LogWarning("WhatsApp webhook requires recovery. EventId={EventId} Attempt={Attempt} Category={Category}",
                job.Id, job.ProcessingAttempts, ex.GetType().Name);
        }
        finally
        {
            Duration.Record(started.Elapsed.TotalMilliseconds);
            if (!ct.IsCancellationRequested)
            {
                using var finish = scopes.CreateScope();
                var accepted = await finish.ServiceProvider.GetRequiredService<WhatsAppProcessingStore>().FinishAsync(job, succeeded, ct);
                if (accepted && succeeded)
                {
                    Completed.Add(1);
                    using var finishTenant = finish.ServiceProvider.GetRequiredService<ITenantExecutionContext>().BeginTenantScope(job.TenantId);
                    var db = finish.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var incoming = await db.WhatsAppMessages.AsNoTracking().Where(x => job.WhatsAppMessageId != null
                        && job.WhatsAppMessageId != "" && x.WhatsAppMessageId == job.WhatsAppMessageId
                        && x.Direction == Domain.Enums.WhatsAppMessageDirection.Inbound
                        && x.AiProcessingStatus == Domain.Enums.WhatsAppAiProcessingStatus.Pending)
                        .Select(x => new { x.Id, x.ConversationId }).FirstOrDefaultAsync(ct);
                    if (incoming is not null)
                        finish.ServiceProvider.GetRequiredService<IWhatsAppAiWorkQueue>().TryEnqueue(incoming.ConversationId, incoming.Id);
                }
            }
        }
        return true;
    }
}
