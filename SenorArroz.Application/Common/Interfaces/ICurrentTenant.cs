namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Tenant established by trusted backend authentication or server configuration.</summary>
public interface ICurrentTenant
{
    int TenantId { get; }
    bool HasTenant { get; }
}
