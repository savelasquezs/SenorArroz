namespace SenorArroz.Application.Features.WhatsApp.DTOs;

public sealed record WhatsAppMessagePageDto(IReadOnlyList<WhatsAppMessageDto> Items, string? NextCursor, bool HasMore);

public class WhatsAppBranchSettingDto
{
    public int? Id { get; set; }
    public int BranchId { get; set; }
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public bool AccessTokenConfigured { get; set; }
    public string? AccessTokenMasked { get; set; }
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public bool AppSecretConfigured { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public bool AwayMessageEnabled { get; set; }
    public string? AwayMessageText { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = "not_configured";
}

public class UpsertWhatsAppBranchSettingDto
{
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string? AppSecret { get; set; }
    public bool IsActive { get; set; }
    public bool? AwayMessageEnabled { get; set; }
    public string? AwayMessageText { get; set; }
}

public class WhatsAppTestConnectionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public WhatsAppBranchSettingDto? Setting { get; set; }
}

public class WhatsAppStatusDto
{
    public bool Enabled { get; set; }
    public IReadOnlyList<int> BranchIds { get; set; } = [];
}

public class WhatsAppUnreadSummaryDto
{
    public int TotalUnread { get; set; }
    public int UnreadConversations { get; set; }
    public DateTime? LatestMessageAt { get; set; }
}

public class WhatsAppTemplateDto
{
    public int Id { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? BusinessAccountId { get; set; }
    public string MetaTemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Components { get; set; } = "[]";
    public int BodyParameterCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WhatsAppTemplateSearchDto
{
    public int? BranchId { get; set; }
    public string? Status { get; set; } = "APPROVED";
    public string? Search { get; set; }
}

public class SyncWhatsAppTemplatesDto
{
    public int? BranchId { get; set; }
}

public class WhatsAppTemplateSyncResultDto
{
    public int Synced { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
}

public class SendWhatsAppTemplateDto
{
    public int? BranchId { get; set; }
    public string? To { get; set; }
    public List<int>? CustomerIds { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = [];
}

public class WhatsAppTemplateSendResultDto
{
    public bool Success { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> MessageIds { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public class WhatsAppQuickReplyDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public string MessageTemplate { get; set; } = string.Empty;
    public string ReplyMode { get; set; } = "send";
    public bool IsActive { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WhatsAppQuickReplySearchDto
{
    public int? BranchId { get; set; }
    public bool ActiveOnly { get; set; } = false;
    public string? Search { get; set; }
}

public class UpsertWhatsAppQuickReplyDto
{
    public int? BranchId { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public string MessageTemplate { get; set; } = string.Empty;
    public string? ReplyMode { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record WhatsAppQuickReplyPreviewRequest(string? Family, string? Size);
public sealed record WhatsAppQuickReplyPresentationDto(string Family, string Size);
public sealed record WhatsAppQuickReplyPreviewDto(string? Text, string? Family = null, string? Size = null,
    IReadOnlyList<WhatsAppQuickReplyPresentationDto>? Presentations = null);

public sealed class UpdateWhatsAppOperationalBranchDto
{
    public long? ExpectedVersion { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("branchId")]
    public int? OperationalBranchId { get; set; }
}

public sealed class WhatsAppOperationalBranchOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class WhatsAppConversationDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Labels { get; set; }
    public string? CommercialStatus { get; set; }
    public int? CommercialOrderId { get; set; }
    public string? SenderLine { get; set; }
    public long AttentionRevision { get; set; }
    public bool NeedsHumanResponse { get; set; }
    public DateTime? LastCustomerMessageAt { get; set; }
    public DateTime? CustomerServiceWindowExpiresAt { get; set; }
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public bool IsCentralChannel { get; set; }
    public int? OperationalBranchId { get; set; }
    public string? OperationalBranchName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? WhatsAppUsername { get; set; }
    public bool HasWhatsAppIdentity { get; set; }
    public string? ContactName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public string AttentionMode { get; set; } = "ai";
    public string? AttentionReason { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime? AiPausedAt { get; set; }
    public DateTime? HumanAssignedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime AttentionModeUpdatedAt { get; set; }
    public int? AttentionModeUpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WhatsAppAttentionDto
{
    public long AttentionRevision { get; set; }
    public bool NeedsHumanResponse { get; set; }
    public int ConversationId { get; set; }
    public string AttentionMode { get; set; } = "ai";
    public string? AttentionReason { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTime? AiPausedAt { get; set; }
    public DateTime? HumanAssignedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime AttentionModeUpdatedAt { get; set; }
    public int? AttentionModeUpdatedByUserId { get; set; }
}

public class WhatsAppMessageDto
{
    public bool IsUpdate { get; set; }
    public DateTime? EditedAt { get; set; }
    public string? HiddenSource { get; set; }
    public string? ReplyToWhatsAppMessageId { get; set; }
    public WhatsAppMessageLocationDto? Location { get; set; }
    public IReadOnlyList<WhatsAppMessageContactDto>? Contacts { get; set; }
    public string? CustomerReaction { get; set; }
    public string? BusinessReaction { get; set; }
    public bool DraftAddressApplied { get; set; }
    public bool DraftProductApplied { get; set; }
    public string Source { get; set; } = "api";
    public bool IsHistorical { get; set; }
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TextBody { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaMimeType { get; set; }
    public string? MediaFileName { get; set; }
    public long? MediaFileSize { get; set; }
    public string? MediaSha256 { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? SentByUserId { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public WhatsAppAiProcessingDto? AiProcessing { get; set; }
}

public class WhatsAppAiDiagnosticsDto
{
    public int BranchId { get; set; }
    public int? ConversationId { get; set; }
    public string AgentStatus { get; set; } = "not_configured";
    public string OverallStatus { get; set; } = "idle";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public string? AttentionMode { get; set; }
    public int PendingCount { get; set; }
    public int FailedCountLast24Hours { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public IReadOnlyList<WhatsAppAiProcessingDto> RecentMessages { get; set; } = [];
}

public class WhatsAppAiProcessingDto
{
    public int MessageId { get; set; }
    public int ConversationId { get; set; }
    public string Status { get; set; } = "notApplicable";
    public string Severity { get; set; } = "neutral";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? TechnicalDetail { get; set; }
    public string? ErrorCategory { get; set; }
    public int? HttpStatusCode { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public bool WillRetry { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime StatusChangedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class WhatsAppOrderDraftDto
{
    public int ConversationId { get; set; }
    public int BranchId { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? WhatsAppUsername { get; set; }
    public string? OrderType { get; set; }
    public int? SelectedAddressId { get; set; }
    public WhatsAppOrderDraftAddressDto? SelectedAddress { get; set; }
    public IReadOnlyList<WhatsAppOrderDraftItemDto> Items { get; set; } = [];
    public IReadOnlyList<WhatsAppOrderDraftActivityDto> Activities { get; set; } = [];
    public int Subtotal { get; set; }
    public int DeliveryFee { get; set; }
    public int Total { get; set; }
    public int TotalItems { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public record WhatsAppOrderDraftItemDto(int ProductId, string Name, int Quantity, int UnitPrice, int Subtotal, string? Notes, bool Available);
public record WhatsAppOrderDraftAddressDto(int Id, string Address, string? AdditionalInfo, string? Neighborhood, int DeliveryFee);
public record WhatsAppOrderDraftActivityDto(string Type, string Message, DateTime Timestamp);
public class UpdateWhatsAppOrderDraftFulfillmentDto { public string OrderType { get; set; } = string.Empty; public int? AddressId { get; set; } }

public sealed record WhatsAppConversationPageDto(IReadOnlyList<WhatsAppConversationDto> Items, string? NextCursor, bool HasMore);

public sealed record WhatsAppInboxLabelDto(string Key, string Name, bool Editable, string? Color = null, bool Custom = false);
public sealed record WhatsAppInboxLabelDefinitionDto(string? Name, string? Color);
public sealed record WhatsAppInboxTabCountsDto(int PendingOrders, int UnreadConversations);
public sealed record WhatsAppInboxLabelWriteDto(bool Applied);
public sealed record WhatsAppInboxTakenResetDto(int? BranchId, bool UnroutedOnly = false);

public class WhatsAppConversationSearchDto
{
    public string? Label { get; set; }
    public string? Cursor { get; set; }
    public int Limit { get; set; } = 50;
    public string? Inbox { get; set; }
    public bool? UnroutedOnly { get; set; }
    public int? BranchId { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public bool? UnreadOnly { get; set; }
}

public sealed class WhatsAppAttentionActionDto
{
    public string Action { get; set; } = string.Empty;
    public long? ExpectedVersion { get; set; }
    public int? TargetUserId { get; set; }
    public string? Reason { get; set; }
}

public class SendWhatsAppMessageDto
{
    public string Text { get; set; } = string.Empty;
    public int? ReplyToMessageId { get; set; }
}

public sealed record WhatsAppMessageLocationDto(double Latitude, double Longitude, string? Name, string? Address);
public sealed record WhatsAppMessageContactDto(string Name, string[] Phones);
public sealed record WhatsAppMessageReactionDto(string Emoji);

public sealed class SendConversationWhatsAppTemplateDto
{
    public string TemplateName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = [];
}

public class SendWhatsAppQuickReplyDto
{
    public int QuickReplyId { get; set; }
}

public class LinkWhatsAppConversationCustomerDto
{
    public int CustomerId { get; set; }
    public long? ExpectedAttentionVersion { get; set; }
    public string? PhoneNumber { get; set; }
}
