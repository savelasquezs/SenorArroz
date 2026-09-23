using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.API.Hubs;

public static class WhatsAppRealtimeGroupResolver
{
    public static string User(int tenantId, int userId) => TenantRealtimeGroups.User(tenantId, userId, "WhatsApp");

    public static IReadOnlyList<string> Resolve(int tenantId, int legacyBranchId, WhatsAppConversationDto conversation)
    {
        var groups = new List<string>();

        if (!conversation.IsCentralChannel)
        {
            groups.Add(TenantRealtimeGroups.BranchRole(tenantId, legacyBranchId, "WhatsApp"));
        }
        else if (!conversation.OperationalBranchId.HasValue)
        {
            groups.Add(TenantRealtimeGroups.TenantChannel(tenantId, "WhatsApp_Unassigned"));
        }
        else
        {
            groups.Add(TenantRealtimeGroups.BranchRole(tenantId, conversation.OperationalBranchId.Value, "WhatsApp"));
            groups.Add(TenantRealtimeGroups.TenantChannel(tenantId, "WhatsApp_Superadmin"));
        }

        if (conversation.AssignedUserId.HasValue)
            groups.Add(User(tenantId, conversation.AssignedUserId.Value));

        return groups.Distinct().ToArray();
    }
}
