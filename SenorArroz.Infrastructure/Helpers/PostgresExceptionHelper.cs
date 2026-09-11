using Npgsql;

namespace SenorArroz.Infrastructure.Helpers;

public static class PostgresExceptionHelper
{
    public static bool IsTransactionConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
                return true;
        }
        return false;
    }
}
