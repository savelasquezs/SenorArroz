using System.Collections.Concurrent;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Services;

public sealed class TenantConnectionRegistry : ITenantConnectionRegistry
{
    private sealed class Registration(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose();
        }
    }

    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, Action>> _connections = new();

    public IDisposable Register(int tenantId, string connectionId, Action abort)
    {
        var tenantConnections = _connections.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, Action>());
        tenantConnections[connectionId] = abort;
        return new Registration(() =>
        {
            if (!tenantConnections.TryRemove(connectionId, out _)) return;
            if (tenantConnections.IsEmpty) _connections.TryRemove(new KeyValuePair<int, ConcurrentDictionary<string, Action>>(tenantId, tenantConnections));
        });
    }

    public void Revoke(int tenantId)
    {
        if (!_connections.TryRemove(tenantId, out var tenantConnections)) return;
        foreach (var abort in tenantConnections.Values)
        {
            try { abort(); }
            catch { }
        }
    }
}
