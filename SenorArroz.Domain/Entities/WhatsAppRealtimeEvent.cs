using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class WhatsAppRealtimeEvent : TenantOwnedEntity
{
    public int ConversationId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string[] Groups { get; set; } = [];
    public string[] DeliveredTo { get; set; } = [];
}
