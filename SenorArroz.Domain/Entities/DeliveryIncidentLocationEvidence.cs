using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryIncidentLocationEvidence
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public long SourceLocationId { get; set; }
    public Guid? ClientPointId { get; set; }
    public bool IsCorePoint { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? HeadingDegrees { get; set; }
    public int? BatteryLevelPercent { get; set; }
    public bool? InternetAvailable { get; set; }
    public bool? GpsEnabled { get; set; }
    public DeliveryTrackingMode? TrackingMode { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime? SyncedAt { get; set; }

    public virtual DeliveryTrackingIncident Incident { get; set; } = null!;
}
