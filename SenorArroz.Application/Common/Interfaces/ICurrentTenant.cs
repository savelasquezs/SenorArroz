namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Tenant established by trusted backend authentication or server configuration.</summary>
public interface ICurrentTenant
{
    int TenantId { get; }
    Guid? TenantPublicId { get; }
    long? AccessVersion { get; }
    bool HasTenant { get; }
}
