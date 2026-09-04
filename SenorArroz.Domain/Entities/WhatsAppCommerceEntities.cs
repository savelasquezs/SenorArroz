using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class WhatsAppChannelSetting : BaseEntity
{
    public int TenantId { get; set; } = 1;
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string? AppSecret { get; set; }
    public string? FlowId { get; set; }
    public string FlowJsonVersion { get; set; } = "7.1";
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public bool FlowEnabled { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public bool AwayMessageEnabled { get; set; }
    public string? AwayMessageText { get; set; }

    public ICollection<WhatsAppCommerceSession> CommerceSessions { get; set; } = [];
}

public sealed class TenantAiSetting : BaseEntity
{
    public int TenantId { get; set; } = 1;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public double? Temperature { get; set; }
    public int MaxContextMessages { get; set; } = 20;
    public DateTime? LastTestedAt { get; set; }
    public bool IsVerified { get; set; }
    public string AssistantName { get; set; } = string.Empty;
    public string? PromptObjective { get; set; }
    public string? PromptPersonality { get; set; }
    public string? PromptRequiredRules { get; set; }
    public string? PromptFixedBranchInfo { get; set; }
    public string? PromptAdditionalInstructions { get; set; }
    public string TransferMessage { get; set; } = "Un asesor continuará con tu atención.";
}

public sealed class WhatsAppCommerceSession : BaseEntity
{
    public int TenantId { get; set; } = 1;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public int ChannelSettingId { get; set; }
    public int ConversationId { get; set; }
    public int? BranchId { get; set; }
    public int? CustomerId { get; set; }
    public string FlowTokenHash { get; set; } = string.Empty;
    public string StateJson { get; set; } = "{}";
    public string Status { get; set; } = "active";
    public int Version { get; set; } = 1;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public WhatsAppChannelSetting ChannelSetting { get; set; } = null!;
    public WhatsAppConversation Conversation { get; set; } = null!;
    public Branch? Branch { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<WhatsAppFlowExchange> Exchanges { get; set; } = [];
    public ICollection<WhatsAppCommerceEvent> Events { get; set; } = [];
}

public sealed class WhatsAppFlowExchange : BaseEntity
{
    public int SessionId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = "{}";
    public WhatsAppCommerceSession Session { get; set; } = null!;
}

public sealed class WhatsAppCommerceOutboxMessage : BaseEntity
{
    public int TenantId { get; set; } = 1;
    public int ChannelSettingId { get; set; }
    public int ConversationId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ButtonText { get; set; }
    public string? Url { get; set; }
    public string Status { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }
    public WhatsAppChannelSetting ChannelSetting { get; set; } = null!;
    public WhatsAppConversation Conversation { get; set; } = null!;
}

public sealed class WhatsAppCommerceEvent : BaseEntity
{
    public int TenantId { get; set; } = 1;
    public int SessionId { get; set; }
    public int ConversationId { get; set; }
    public int? BranchId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string? Screen { get; set; }
    public string? ReferenceId { get; set; }
    public WhatsAppCommerceSession Session { get; set; } = null!;
    public WhatsAppConversation Conversation { get; set; } = null!;
    public Branch? Branch { get; set; }
}
