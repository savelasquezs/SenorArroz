using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryIncidentDeviceEventEvidence
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public long SourceDeviceEventId { get; set; }
    public Guid? ClientEventId { get; set; }
    public DeliveryDeviceEventType EventType { get; set; }
    public int? BatteryLevelPercent { get; set; }
    public bool? InternetAvailable { get; set; }
    public bool? GpsEnabled { get; set; }
    public bool? LocationPermissionGranted { get; set; }
    public string? Details { get; set; }
    public int? OfflineLocationCount { get; set; }
    public DateTime? OfflineStartedAt { get; set; }
    public DateTime? OfflineEndedAt { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime SyncedAt { get; set; }

    public virtual DeliveryTrackingIncident Incident { get; set; } = null!;
}
