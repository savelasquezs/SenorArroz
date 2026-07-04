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
    public WhatsAppMessageStatus Status { get; set; }
    public int? SentByUserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? RawPayload { get; set; }

    public virtual WhatsAppConversation Conversation { get; set; } = null!;
    public virtual User? SentByUser { get; set; }
}
