using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class WhatsAppMessage : BaseEntity
{
    public int ConversationId { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public WhatsAppMessageDirection Direction { get; set; }
    public WhatsAppMessageType Type { get; set; } = WhatsAppMessageType.Text;
    public string TextBody { get; set; } = string.Empty;
    public string? MediaId { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaMimeType { get; set; }
    public string? MediaFileName { get; set; }
    public long? MediaFileSize { get; set; }
    public string? MediaSha256 { get; set; }
    public WhatsAppMessageStatus Status { get; set; }
    public int? SentByUserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? RawPayload { get; set; }
    public WhatsAppAiProcessingStatus AiProcessingStatus { get; set; } = WhatsAppAiProcessingStatus.NotApplicable;
    public DateTime? AiProcessedAt { get; set; }
    public int AiProcessingAttempts { get; set; }
    public string? AiProcessingError { get; set; }
    public bool SentByAi { get; set; }
    public DateTime? AiProcessingStartedAt { get; set; }
    public DateTime? AiNextRetryAt { get; set; }
    public string? AiGeneratedResponse { get; set; }
    public string? AiResponseAttemptId { get; set; }
    public string? AiResponseWhatsAppMessageId { get; set; }
    public string? AgentDispatchKey { get; set; }

    public virtual WhatsAppConversation Conversation { get; set; } = null!;
    public virtual User? SentByUser { get; set; }
}
