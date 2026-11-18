using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class WhatsAppConversation : TenantOwnedEntity
{
    public int? ChannelSettingId { get; set; }
    public int? BranchLineId { get; set; }
    public int BranchId { get; set; }
    public int? OperationalBranchId { get; set; }
    public int? CustomerId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? WhatsAppUserId { get; set; }
    public string? WhatsAppUsername { get; set; }
    public string? ContactName { get; set; }
    public WhatsAppConversationStatus Status { get; set; } = WhatsAppConversationStatus.Open;
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public DateTime? LastCustomerMessageAt { get; set; }
    public bool IsSupplier { get; set; }
    public DateTime? PendingOrderMarkedAt { get; set; }
    public bool IsPaymentPending { get; set; }
    public bool NeedsHumanResponse { get; set; }
    public long AttentionRevision { get; set; }
    public WhatsAppAttentionMode AttentionMode { get; set; } = WhatsAppAttentionMode.Ai;
    public int? AssignedUserId { get; set; }
    public DateTime? AiPausedAt { get; set; }
    public DateTime? HumanAssignedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime AttentionModeUpdatedAt { get; set; }
    public int? AttentionModeUpdatedByUserId { get; set; }
    public string? AiOrderState { get; set; }
    public DateTime? AiOrderStateUpdatedAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Branch? OperationalBranch { get; set; }
    public virtual WhatsAppChannelSetting? ChannelSetting { get; set; }
    public virtual WhatsAppBranchSetting? BranchLine { get; set; }
    public virtual Customer? Customer { get; set; }
    public virtual User? AssignedUser { get; set; }
    public virtual User? AttentionModeUpdatedByUser { get; set; }
    public virtual ICollection<WhatsAppMessage> Messages { get; set; } = new List<WhatsAppMessage>();
}
