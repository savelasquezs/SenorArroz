using Microsoft.AspNetCore.Http;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public sealed class CurrentTenantService : ICurrentTenant, ITenantExecutionContext
{
    private sealed record ScopeState(int? TenantId, Guid? PublicId, bool SystemAccess);
    private sealed class ScopeHandle(Action dispose) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            dispose();
        }
    }

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AsyncLocal<ScopeState?> _scope = new();

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public int TenantId => Resolve().TenantId ?? 0;
    public Guid? TenantPublicId => Resolve().PublicId;
    public bool HasTenant => TenantId > 0;
    public bool CanAccessAllTenants => Resolve().SystemAccess;

    public IDisposable BeginTenantScope(int tenantId, Guid? publicId = null)
    {
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        return Push(new ScopeState(tenantId, publicId, false));
    }

    public IDisposable BeginSystemScope() => Push(new ScopeState(null, null, true));

    private ScopeState Resolve()
    {
        if (_scope.Value is not null) return _scope.Value;
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null) return new ScopeState(null, null, true);
        if (httpContext.User.Identity?.IsAuthenticated != true) return new ScopeState(null, null, false);

        var tenantValue = httpContext.User.FindFirst("tenant_id")?.Value;
        var publicValue = httpContext.User.FindFirst("tenant_public_id")?.Value;
        return new ScopeState(
            int.TryParse(tenantValue, out var tenantId) && tenantId > 0 ? tenantId : null,
            Guid.TryParse(publicValue, out var publicId) ? publicId : null,
            false);
    }

    private IDisposable Push(ScopeState state)
    {
        var previous = _scope.Value;
        _scope.Value = state;
        return new ScopeHandle(() => _scope.Value = previous);
    }
}

public sealed class PlatformCurrentUser : IPlatformCurrentUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
}
