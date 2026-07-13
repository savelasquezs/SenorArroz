using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed class WhatsAppAiTelemetryWorkerOptions
{
    public const string SectionName = "WhatsAppAiTelemetry";
    public int DrainTimeoutSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 50;
}

public sealed class WhatsAppAiTelemetryWorker(
    WhatsAppAiTelemetryQueue queue,
    IDbContextFactory<ApplicationDbContext> factory,
    IOptions<WhatsAppAiTelemetryWorkerOptions> options,
    ILogger<WhatsAppAiTelemetryWorker> logger) : IHostedService
{
    private readonly CancellationTokenSource _forceStop = new();
    private Task? _execution;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _execution = RunAsync(_forceStop.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        if (_execution is null) return;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.DrainTimeoutSeconds));
        var completed = await Task.WhenAny(_execution, Task.Delay(timeout, cancellationToken));
        if (completed != _execution)
        {
            logger.LogWarning("WhatsApp AI telemetry drain exceeded {DrainTimeoutSeconds}s; forcing shutdown", timeout.TotalSeconds);
            _forceStop.Cancel();
        }
        try { await _execution.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (_forceStop.IsCancellationRequested || cancellationToken.IsCancellationRequested) { }
    }

    private async Task RunAsync(CancellationToken forceStop)
    {
        var batch = new List<WhatsAppAiInvocation>(Math.Max(1, options.Value.BatchSize));
        try
        {
            while (await queue.Reader.WaitToReadAsync(forceStop))
            {
                batch.Clear();
                while (batch.Count < Math.Max(1, options.Value.BatchSize) && queue.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count == 0) continue;
                try
                {
                    await using var db = await factory.CreateDbContextAsync(forceStop);
                    db.WhatsAppAiInvocations.AddRange(batch);
                    await db.SaveChangesAsync(forceStop);
                }
                catch (Exception ex) when (!forceStop.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Could not persist WhatsApp AI telemetry batch Count={Count}; batch discarded", batch.Count);
                }
            }
        }
        catch (OperationCanceledException) when (forceStop.IsCancellationRequested) { }
    }
}
