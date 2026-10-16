using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class PostgresDeliveryAutoCompletionRouteLock(
    ApplicationDbContext db,
    IOrderMutationContext? mutation = null,
    ILogger<PostgresDeliveryAutoCompletionRouteLock>? logger = null)
    : IDeliveryAutoCompletionRouteLock
{
    private const int AdvisoryLockNamespace = 1_397_905_218;

    public async Task ExecuteAsync(int routeId, Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        var ownsTransaction = db.Database.CurrentTransaction is null;
        var ownsNotifications = ownsTransaction && mutation is not null && !mutation.IsDeferringNotifications;
        await using var transaction = db.Database.IsNpgsql() && ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (ownsNotifications) mutation!.BeginDeferredNotifications();

        try
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {routeId})",
                    cancellationToken);
            await action(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (ownsNotifications)
                foreach (var send in mutation!.DrainDeferredNotifications())
                {
                    try { await send(); }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Route {RouteId} committed; notification failed", routeId);
                    }
                }
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownsNotifications) mutation!.ClearDeferredNotifications();
        }
    }
}
