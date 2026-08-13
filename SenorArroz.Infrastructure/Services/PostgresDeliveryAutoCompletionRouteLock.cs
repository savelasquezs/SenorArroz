using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class PostgresDeliveryAutoCompletionRouteLock(ApplicationDbContext db)
    : IDeliveryAutoCompletionRouteLock
{
    private const int AdvisoryLockNamespace = 1_397_905_218;

    public async Task ExecuteAsync(
        int routeId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
        {
            await action(cancellationToken);
            return;
        }

        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {routeId})",
                cancellationToken);
            await action(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
