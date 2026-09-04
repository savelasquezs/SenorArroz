using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppConversationMapper
{
    public static WhatsAppConversationDto ToDto(WhatsAppConversation conversation, string? assignedUserName = null, string? attentionReason = null) => new()
    {
        Id = conversation.Id,
        BranchId = conversation.BranchId,
        BranchName = conversation.Branch?.Name,
        IsCentralChannel = conversation.ChannelSettingId.HasValue,
        OperationalBranchId = conversation.OperationalBranchId,
        OperationalBranchName = conversation.OperationalBranch?.Name,
        CustomerId = conversation.CustomerId,
        CustomerName = conversation.Customer?.Name,
        PhoneNumber = conversation.PhoneNumber,
        WhatsAppUsername = conversation.WhatsAppUsername,
        HasWhatsAppIdentity = !string.IsNullOrWhiteSpace(conversation.WhatsAppUserId),
        ContactName = conversation.ContactName,
        Status = conversation.Status.ToString().ToLowerInvariant(),
        LastMessageAt = AsUtc(conversation.LastMessageAt),
        LastMessagePreview = conversation.LastMessagePreview,
        UnreadCount = conversation.UnreadCount,
        AttentionMode = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman ? "waitingForHuman" : conversation.AttentionMode.ToString().ToLowerInvariant(),
        AttentionReason = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman ? attentionReason : null,
        AssignedUserId = conversation.AssignedUserId,
        AssignedUserName = assignedUserName,
        AiPausedAt = AsUtc(conversation.AiPausedAt),
        HumanAssignedAt = AsUtc(conversation.HumanAssignedAt),
        ClosedAt = AsUtc(conversation.ClosedAt),
        AttentionModeUpdatedAt = AsUtc(conversation.AttentionModeUpdatedAt),
        AttentionModeUpdatedByUserId = conversation.AttentionModeUpdatedByUserId,
        CreatedAt = AsUtc(conversation.CreatedAt),
        UpdatedAt = AsUtc(conversation.UpdatedAt)
    };
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
