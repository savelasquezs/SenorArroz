namespace SenorArroz.Application.Common.Interfaces;

public interface ICurrentTenant
{
    int TenantId { get; }
    Guid? TenantPublicId { get; }
    bool HasTenant { get; }
    bool CanAccessAllTenants { get; }
}

public interface ITenantExecutionContext
{
    IDisposable BeginTenantScope(int tenantId, Guid? publicId = null);
    IDisposable BeginSystemScope();
}
