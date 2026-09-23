using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Common.Helpers;

public static class TenantStoragePath
{
    public static string Prefix(ICurrentTenant tenant)
    {
        if (!tenant.HasTenant || tenant.TenantId <= 0)
            throw new InvalidOperationException("Tenant context is required for storage operations.");
        return $"tenants/{tenant.TenantId}";
    }

    public static string Combine(ICurrentTenant tenant, string path) => $"{Prefix(tenant)}/{path.Trim('/')}";
}
