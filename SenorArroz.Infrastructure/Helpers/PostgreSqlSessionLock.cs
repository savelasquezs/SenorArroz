using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace SenorArroz.Infrastructure.Helpers;

internal static class PostgreSqlSessionLock
{
    public static async Task<IAsyncDisposable?> AcquireAsync(
        DatabaseFacade database, int resource, string key, CancellationToken ct)
    {
        if (database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            return null;
        if (database.CurrentTransaction is not null)
            throw new InvalidOperationException("El bloqueo de integración requiere una conexión sin transacción activa.");

        await database.OpenConnectionAsync(ct);
        try
        {
            await database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock({resource}, hashtext({key}))", ct);
            return new Lease(database, resource, key);
        }
        catch
        {
            if (database.GetDbConnection() is NpgsqlConnection connection)
                NpgsqlConnection.ClearPool(connection);
            await database.CloseConnectionAsync();
            throw;
        }
    }

    private sealed class Lease(DatabaseFacade database, int resource, string key) : IAsyncDisposable
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            try
            {
                if (database.GetDbConnection().State == ConnectionState.Open)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_unlock({resource}, hashtext({key}))", timeout.Token);
                }
            }
            catch
            {
                if (database.GetDbConnection() is NpgsqlConnection connection)
                    NpgsqlConnection.ClearPool(connection);
                throw;
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
        }
    }
}
