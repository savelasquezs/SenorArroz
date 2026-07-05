namespace SenorArroz.Application.Features.WhatsApp.DTOs;

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

public class WhatsAppConversationDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WhatsAppMessageDto
{
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
}

public class WhatsAppConversationSearchDto
{
    public int? BranchId { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public bool? UnreadOnly { get; set; }
}

public class SendWhatsAppMessageDto
{
    public string Text { get; set; } = string.Empty;
}

public class LinkWhatsAppConversationCustomerDto
{
    public int CustomerId { get; set; }
}
