using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Keeps GPS/device event insertion independent of the hot work-session heartbeat row.
/// The short heartbeat update runs after the point/event commit, without holding an
/// insert's foreign-key locks while waiting for another transaction's driver lock.
/// </summary>
public static class DeliveryWriteReliability
{
    private const int MaxSaveAttempts = 3;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>Deadlocks and serialization failures are known-aborted PostgreSQL transactions.</summary>
    public static bool IsPostgresConcurrencyFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            // The application assembly does not reference Npgsql; SQLSTATE is provided by the driver.
            if (current.GetType().GetProperty("SqlState")?.GetValue(current) is string state
                && state is "40P01" or "40001")
                return true;
        }

        return false;
    }

    public static async Task SaveWithTransientRetryAsync(IApplicationDbContext db, ILogger? logger,
        int deliverymanId, int workSessionId, string recordKind, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (Exception ex) when (attempt < MaxSaveAttempts && !ct.IsCancellationRequested
                && IsPostgresConcurrencyFailure(ex))
            {
                logger?.LogWarning(ex,
                    "DeliveryRecordRetry Kind={RecordKind} DeliverymanId={DeliverymanId} WorkSessionId={WorkSessionId} Attempt={Attempt}",
                    recordKind, deliverymanId, workSessionId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(75 * attempt), ct);
            }
        }
    }

    /// <summary>
    /// Monotonic, throttled heartbeat. Deliberately outside SaveChanges for the GPS/event insert.
    /// A failed heartbeat must never cause the already-committed point/event to be replayed.
    /// </summary>
    public static async Task TryAdvanceHeartbeatAsync(IApplicationDbContext db, DeliveryWorkSession session,
        DateTime receivedAtUtc, ILogger? logger, CancellationToken ct)
    {
        var staleBefore = receivedAtUtc - HeartbeatInterval;
        if (session.LastCommunicationAt > staleBefore) return;

        try
        {
            if (db.Database.IsRelational())
            {
                await db.DeliveryWorkSessions
                    .Where(x => x.Id == session.Id && x.DeliverymanId == session.DeliverymanId
                        && x.BranchId == session.BranchId && x.LastCommunicationAt <= staleBefore)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(x => x.LastCommunicationAt, receivedAtUtc), ct);
            }
            else
            {
                // InMemory provider used by unit tests; relational providers use a standalone UPDATE.
                session.LastCommunicationAt = receivedAtUtc;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex,
                "DeliveryHeartbeatDeferred DeliverymanId={DeliverymanId} WorkSessionId={WorkSessionId}",
                session.DeliverymanId, session.Id);
        }
    }
}
