using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryDeviceEvent
{
    public long Id { get; set; }
    public int DeliverymanId { get; set; }
    public int WorkSessionId { get; set; }
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User Deliveryman { get; set; } = null!;
    public virtual DeliveryWorkSession WorkSession { get; set; } = null!;

    public static DeliveryDeviceEvent ForClosure(
        DeliveryWorkSession session,
        DateTime nowUtc,
        DeliveryWorkSessionEndReason reason) => new()
    {
        DeliverymanId = session.DeliverymanId,
        WorkSessionId = session.Id,
        EventType = reason == DeliveryWorkSessionEndReason.TotalSettlement
            ? DeliveryDeviceEventType.TotalSettlement
            : DeliveryDeviceEventType.AutomaticClosure,
        RecordedAt = nowUtc,
        SyncedAt = nowUtc,
        InternetAvailable = true,
    };
}
