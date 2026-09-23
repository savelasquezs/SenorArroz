namespace SenorArroz.API.Hubs;

public static class TenantRealtimeGroups
{
    public static string Branch(int tenantId, int branchId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(branchId);
        return $"Tenant_{tenantId}_Branch_{branchId}";
    }
    public static string BranchRole(int tenantId, int branchId, string role) => $"{Branch(tenantId, branchId)}_{role}";
    public static string User(int tenantId, int userId, string channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        return $"Tenant_{tenantId}_User_{userId}_{channel}";
    }
    public static string TenantChannel(int tenantId, string channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        return $"Tenant_{tenantId}_{channel}";
    }
}
