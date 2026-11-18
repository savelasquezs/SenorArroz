using System.Linq.Expressions;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppConversationMapper
{
    public static readonly Expression<Func<WhatsAppConversation, WhatsAppConversationDto>> Projection = conversation => new WhatsAppConversationDto()
    {
        SenderLine = conversation.ChannelSetting != null ? conversation.ChannelSetting.DisplayPhoneNumber : conversation.Branch != null && conversation.Branch.WhatsAppSetting != null ? conversation.Branch.WhatsAppSetting.DisplayPhoneNumber : null,
        AttentionRevision = conversation.AttentionRevision,
        NeedsHumanResponse = conversation.NeedsHumanResponse,
        LastCustomerMessageAt = AsUtc(conversation.LastCustomerMessageAt),
        CustomerServiceWindowExpiresAt = conversation.LastCustomerMessageAt.HasValue ? AsUtc(conversation.LastCustomerMessageAt.Value.AddHours(24)) : null,
        Id = conversation.Id,
        BranchId = conversation.BranchId,
        BranchName = conversation.Branch != null ? conversation.Branch.Name : null,
        IsCentralChannel = conversation.ChannelSettingId.HasValue,
        OperationalBranchId = conversation.OperationalBranchId,
        OperationalBranchName = conversation.OperationalBranch != null ? conversation.OperationalBranch.Name : null,
        CustomerId = conversation.CustomerId,
        CustomerName = conversation.Customer != null ? conversation.Customer.Name : null,
        PhoneNumber = conversation.PhoneNumber,
        WhatsAppUsername = conversation.WhatsAppUsername,
        HasWhatsAppIdentity = !string.IsNullOrWhiteSpace(conversation.WhatsAppUserId),
        ContactName = conversation.ContactName,
        Status = conversation.Status.ToString().ToLowerInvariant(),
        LastMessageAt = AsUtc(conversation.LastMessageAt),
        LastMessagePreview = conversation.LastMessagePreview,
        UnreadCount = conversation.UnreadCount,
        AttentionMode = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman ? "waitingForHuman" : conversation.AttentionMode.ToString().ToLowerInvariant(),
        AttentionReason = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman ? conversation.Messages.Where(x => x.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman && x.AiProcessingError != null).OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id).Select(x => x.AiProcessingError).FirstOrDefault() : null,
        AssignedUserId = conversation.AssignedUserId,
        AssignedUserName = conversation.AssignedUser != null ? conversation.AssignedUser.Name : null,
        AiPausedAt = AsUtc(conversation.AiPausedAt),
        HumanAssignedAt = AsUtc(conversation.HumanAssignedAt),
        ClosedAt = AsUtc(conversation.ClosedAt),
        AttentionModeUpdatedAt = AsUtc(conversation.AttentionModeUpdatedAt),
        AttentionModeUpdatedByUserId = conversation.AttentionModeUpdatedByUserId,
        CreatedAt = AsUtc(conversation.CreatedAt),
        UpdatedAt = AsUtc(conversation.UpdatedAt)
    };
    private static readonly Func<WhatsAppConversation, WhatsAppConversationDto> Map = Projection.Compile();
    public static WhatsAppConversationDto ToDto(WhatsAppConversation conversation, string? assignedUserName = null, string? attentionReason = null)
    {
        var dto = Map(conversation);
        dto.AssignedUserName = assignedUserName ?? dto.AssignedUserName;
        dto.AttentionReason = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman ? attentionReason : null;
        return dto;
    }
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
