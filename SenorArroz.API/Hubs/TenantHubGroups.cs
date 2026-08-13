namespace SenorArroz.API.Hubs;

public static class TenantHubGroups
{
    public static string Branch(int tenantId, int branchId, string? channel = null) =>
        string.IsNullOrWhiteSpace(channel)
            ? $"Tenant_{tenantId}_Branch_{branchId}"
            : $"Tenant_{tenantId}_Branch_{branchId}_{channel}";
}
