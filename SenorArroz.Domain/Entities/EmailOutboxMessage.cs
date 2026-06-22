using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class EmailOutboxMessage : BaseEntity
{
    public string MessageType { get; set; } = string.Empty;
    public string ToEmailsJson { get; set; } = "[]";
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public string Status { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? LastAttemptedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? LastError { get; set; }
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public string MetadataJson { get; set; } = "{}";
}
