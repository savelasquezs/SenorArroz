using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppWebhookEvent : TenantOwnedEntity
{
    public string EventType { get; set; } = string.Empty;
    public string? WhatsAppMessageId { get; set; }
    public string RawPayload { get; set; } = "{}";
    public bool Processed { get; set; }
    public string ProcessingState { get; set; } = "legacy";
    public string? DeduplicationKey { get; set; }
    public string? PartitionKey { get; set; }
    public string? ProtectedPayload { get; set; }
    public int ProcessingAttempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? ProcessingError { get; set; }
}
