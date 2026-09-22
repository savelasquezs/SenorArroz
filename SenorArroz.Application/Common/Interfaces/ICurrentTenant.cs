namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Tenant established by trusted backend authentication or server configuration.</summary>
public interface ICurrentTenant
{
    int TenantId { get; }
    Guid? TenantPublicId { get; }
    long? AccessVersion { get; }
    bool HasTenant { get; }
}

public interface ITenantExecutionContext
{
    bool IsSystemScope { get; }
    IDisposable BeginSystemScope();
    IDisposable BeginTenantScope(int tenantId);
}
