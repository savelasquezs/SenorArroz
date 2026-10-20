namespace SenorArroz.API.Hubs;

public static class EmployeeLeaveRealtime
{
    public static string UserGroup(int tenantId, int userId) => $"tenant:{tenantId}:employee:{userId}";
}
