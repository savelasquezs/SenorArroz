using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed class WhatsAppAiTelemetryWorker(WhatsAppAiTelemetryQueue queue, IDbContextFactory<ApplicationDbContext> factory, ILogger<WhatsAppAiTelemetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<WhatsAppAiInvocation>(50);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();
                while (batch.Count < 50 && queue.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count == 0) continue;
                try
                {
                    await using var db = await factory.CreateDbContextAsync(stoppingToken);
                    db.WhatsAppAiInvocations.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Could not persist WhatsApp AI telemetry batch Count={Count}; batch discarded", batch.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken) { queue.Complete(); await base.StopAsync(cancellationToken); }
}
