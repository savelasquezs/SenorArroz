using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppWebhookEvent : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public string? WhatsAppMessageId { get; set; }
    public string RawPayload { get; set; } = "{}";
    public bool Processed { get; set; }
}
