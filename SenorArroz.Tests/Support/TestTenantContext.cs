using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Tests;

internal sealed class TestTenantContext(int tenantId = 1) : ICurrentTenant, ITenantExecutionContext
{
    private sealed record ScopeState(int TenantId, bool IsSystemScope);

    private sealed class ScopeHandle(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            dispose();
        }
    }

    private readonly AsyncLocal<ScopeState?> _scope = new();

    public static TestTenantContext Default { get; } = new();

    public int TenantId => _scope.Value?.TenantId ?? tenantId;
    public Guid? TenantPublicId => null;
    public long? AccessVersion => null;
    public bool HasTenant => TenantId > 0;
    public bool IsSystemScope => _scope.Value?.IsSystemScope == true;

    public IDisposable BeginSystemScope() => Push(new ScopeState(0, true));

    public IDisposable BeginTenantScope(int scopedTenantId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scopedTenantId);
        return Push(new ScopeState(scopedTenantId, false));
    }

    private IDisposable Push(ScopeState state)
    {
        var previous = _scope.Value;
        _scope.Value = state;
        return new ScopeHandle(() => _scope.Value = previous);
    }
}
