using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryTrackingAlert
{
    public long Id { get; set; }
    public int BranchId { get; set; }
    public int DeliverymanId { get; set; }
    public int? WorkSessionId { get; set; }
    public long? IncidentId { get; set; }
    public long? SourceDeviceEventId { get; set; }
    public long? RecoveryDeviceEventId { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
    public DeliveryTrackingAlertType AlertType { get; set; }
    public DeliveryTrackingAlertSeverity Severity { get; set; }
    public DeliveryTrackingAlertStatus Status { get; set; } = DeliveryTrackingAlertStatus.Active;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime LastOccurredAt { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? StartLatitude { get; set; }
    public decimal? StartLongitude { get; set; }
    public DateTime? StartLocationRecordedAt { get; set; }
    public decimal? EndLatitude { get; set; }
    public decimal? EndLongitude { get; set; }
    public DateTime? EndLocationRecordedAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
