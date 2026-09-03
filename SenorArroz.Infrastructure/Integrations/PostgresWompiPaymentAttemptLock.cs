using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class PostgresWompiPaymentAttemptLock(ApplicationDbContext db) : IWompiPaymentAttemptLock
{
    private const int AdvisoryLockNamespace = 1_461_231_057;

    public async Task<T> ExecuteAsync<T>(
        string reference,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
            return await action(cancellationToken);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, hashtext({reference}))",
                cancellationToken);
            var result = await action(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
