using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Data;

internal static class TenantDbSession
{
    private const string SetContextSql = """
        SELECT
            set_config('app.current_tenant_id', @tenant_id, false),
            set_config('app.system_scope', @system_scope, false)
        """;

    private const string ResetContextSql = """
        SELECT
            set_config('app.current_tenant_id', '', false),
            set_config('app.system_scope', 'off', false)
        """;

    public static void ValidatePooling(DbConnection connection)
    {
        if (connection is NpgsqlConnection npgsql
            && new NpgsqlConnectionStringBuilder(npgsql.ConnectionString).NoResetOnClose)
        {
            throw new InvalidOperationException(
                "PostgreSQL No Reset On Close debe permanecer deshabilitado para proteger el contexto tenant del pool.");
        }
    }

    public static void Apply(
        DbConnection connection,
        DbTransaction? transaction,
        ICurrentTenant currentTenant,
        ITenantExecutionContext executionContext)
    {
        if (connection is not NpgsqlConnection || connection.State != ConnectionState.Open)
            return;

        using var command = CreateCommand(connection, transaction, SetContextSql);
        AddContextParameters(command, currentTenant, executionContext);
        command.ExecuteNonQuery();
    }

    public static async Task ApplyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ICurrentTenant currentTenant,
        ITenantExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (connection is not NpgsqlConnection || connection.State != ConnectionState.Open)
            return;

        await using var command = CreateCommand(connection, transaction, SetContextSql);
        AddContextParameters(command, currentTenant, executionContext);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static void Reset(DbConnection connection)
    {
        if (connection is not NpgsqlConnection || connection.State != ConnectionState.Open)
            return;

        using var command = CreateCommand(connection, null, ResetContextSql);
        command.ExecuteNonQuery();
    }

    public static async Task ResetAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not NpgsqlConnection || connection.State != ConnectionState.Open)
            return;

        await using var command = CreateCommand(connection, null, ResetContextSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;
        return command;
    }

    private static void AddContextParameters(
        DbCommand command,
        ICurrentTenant currentTenant,
        ITenantExecutionContext executionContext)
    {
        var tenantParameter = command.CreateParameter();
        tenantParameter.ParameterName = "tenant_id";
        tenantParameter.Value = executionContext.IsSystemScope || !currentTenant.HasTenant
            ? string.Empty
            : currentTenant.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        command.Parameters.Add(tenantParameter);

        var systemParameter = command.CreateParameter();
        systemParameter.ParameterName = "system_scope";
        systemParameter.Value = executionContext.IsSystemScope ? "on" : "off";
        command.Parameters.Add(systemParameter);
    }
}

public sealed class TenantDbCommandInterceptor(
    ICurrentTenant currentTenant,
    ITenantExecutionContext executionContext) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        TenantDbSession.Apply(command.Connection!, command.Transaction, currentTenant, executionContext);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await TenantDbSession.ApplyAsync(command.Connection!, command.Transaction, currentTenant, executionContext, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        TenantDbSession.Apply(command.Connection!, command.Transaction, currentTenant, executionContext);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await TenantDbSession.ApplyAsync(command.Connection!, command.Transaction, currentTenant, executionContext, cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        TenantDbSession.Apply(command.Connection!, command.Transaction, currentTenant, executionContext);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await TenantDbSession.ApplyAsync(command.Connection!, command.Transaction, currentTenant, executionContext, cancellationToken);
        return result;
    }
}

public sealed class TenantDbConnectionInterceptor(
    ICurrentTenant currentTenant,
    ITenantExecutionContext executionContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        TenantDbSession.ValidatePooling(connection);
        TenantDbSession.Apply(connection, null, currentTenant, executionContext);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TenantDbSession.ValidatePooling(connection);
        await TenantDbSession.ApplyAsync(connection, null, currentTenant, executionContext, cancellationToken);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        TenantDbSession.Reset(connection);
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await TenantDbSession.ResetAsync(connection, CancellationToken.None);
        return result;
    }
}
