using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.API.Hubs;

public static class WhatsAppRealtimeGroupResolver
{
    public static string User(int userId) => $"User_{userId}_WhatsApp";

    public static IReadOnlyList<string> Resolve(int legacyBranchId, WhatsAppConversationDto conversation)
    {
        var groups = new List<string>();

        if (!conversation.IsCentralChannel)
        {
            groups.Add($"Branch_{legacyBranchId}_WhatsApp");
        }
        else if (!conversation.OperationalBranchId.HasValue)
        {
            groups.Add("Tenant_1_WhatsApp_Unassigned");
        }
        else
        {
            groups.Add($"Branch_{conversation.OperationalBranchId.Value}_WhatsApp");
            groups.Add("Tenant_1_WhatsApp_Superadmin");
        }

        if (conversation.AssignedUserId.HasValue)
            groups.Add(User(conversation.AssignedUserId.Value));

        return groups.Distinct().ToArray();
    }
}
