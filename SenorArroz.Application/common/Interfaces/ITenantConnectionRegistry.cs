namespace SenorArroz.Application.Common.Interfaces;

public interface ITenantConnectionRegistry
{
    IDisposable Register(int tenantId, string connectionId, Action abort);
    void Revoke(int tenantId);
}
