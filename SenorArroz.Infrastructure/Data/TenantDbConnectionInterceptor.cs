using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Data;

public sealed class TenantDbConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentTenant _currentTenant;
    public TenantDbConnectionInterceptor(ICurrentTenant currentTenant) => _currentTenant = currentTenant;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Apply(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, cancellationToken);

    private void Apply(DbConnection connection)
    {
        if (connection is not NpgsqlConnection npgsql) return;
        using var command = npgsql.CreateCommand();
        command.CommandText = "select set_config('app.current_tenant_id', @tenant, false), set_config('app.tenant_bypass', @bypass, false)";
        command.Parameters.AddWithValue("tenant", _currentTenant.HasTenant ? _currentTenant.TenantId.ToString() : string.Empty);
        command.Parameters.AddWithValue("bypass", _currentTenant.CanAccessAllTenants ? "true" : "false");
        command.ExecuteNonQuery();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not NpgsqlConnection npgsql) return;
        await using var command = npgsql.CreateCommand();
        command.CommandText = "select set_config('app.current_tenant_id', @tenant, false), set_config('app.tenant_bypass', @bypass, false)";
        command.Parameters.AddWithValue("tenant", _currentTenant.HasTenant ? _currentTenant.TenantId.ToString() : string.Empty);
        command.Parameters.AddWithValue("bypass", _currentTenant.CanAccessAllTenants ? "true" : "false");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
