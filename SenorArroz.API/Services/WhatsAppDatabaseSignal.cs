using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;
using SenorArroz.Application.Options;

namespace SenorArroz.API.Services;

public sealed class WhatsAppDatabaseSignal(IConfiguration configuration,
    IOptions<WhatsAppProcessingOptions> options, ILogger<WhatsAppDatabaseSignal> logger) : BackgroundService
{
    private TaskCompletionSource _changed = NewSignal();
    private long _version;
    public long Version => Interlocked.Read(ref _version);
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitAsync(long observedVersion, CancellationToken ct)
    {
        var changed = _changed.Task;
        if (observedVersion != Version) return;
        try { await changed.WaitAsync(TimeSpan.FromSeconds(options.Value.PollSeconds), ct); }
        catch (TimeoutException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection")) { Pooling = false };
                await using var connection = new NpgsqlConnection(settings.ConnectionString);
                connection.Notification += (_, _) => Pulse();
                await connection.OpenAsync(ct);
                await using var listen = new NpgsqlCommand("LISTEN whatsapp_processing_changed", connection);
                await listen.ExecuteNonQueryAsync(ct);
                Pulse();
                while (!ct.IsCancellationRequested) await connection.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("WhatsApp wakeup connection interrupted. Category={Category}", ex.GetType().Name);
                Pulse();
                try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }

    private void Pulse()
    {
        Interlocked.Increment(ref _version);
        Interlocked.Exchange(ref _changed, NewSignal()).TrySetResult();
    }

    public static async Task NotifyAsync(DatabaseFacade database, ILogger logger, CancellationToken ct)
    {
        try { await database.ExecuteSqlRawAsync("SELECT pg_notify('whatsapp_processing_changed', '')", ct); }
        catch (Exception ex)
        {
            logger.LogWarning("WhatsApp work persisted; wakeup will use recovery. Category={Category}", ex.GetType().Name);
        }
    }
}
