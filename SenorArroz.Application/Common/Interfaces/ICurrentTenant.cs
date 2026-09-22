namespace SenorArroz.Application.Common.Interfaces;

public interface ICurrentTenant
{
    int TenantId { get; }
    Guid? TenantPublicId { get; }
    long? AccessVersion { get; }
    bool HasTenant { get; }
}
