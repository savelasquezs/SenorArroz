using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Punto GPS registrado por un domiciliario durante una jornada laboral.
/// </summary>
public class DeliverymanLocation
{
    public long Id { get; set; }
    public int DeliverymanId { get; set; }
    public int? WorkSessionId { get; set; }
    public int? DeliveryRouteId { get; set; }
    public Guid? ClientPointId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? HeadingDegrees { get; set; }
    public int? BatteryLevelPercent { get; set; }
    public bool? InternetAvailable { get; set; }
    public bool? GpsEnabled { get; set; }
    public DeliveryTrackingMode? TrackingMode { get; set; }

    /// <summary>Momento exacto de la captura GPS en el dispositivo (UTC).</summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>Momento UTC en que el servidor recibió el punto.</summary>
    public DateTime? SyncedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User Deliveryman { get; set; } = null!;
    public virtual DeliveryWorkSession? WorkSession { get; set; }
    public virtual DeliveryRoute? DeliveryRoute { get; set; }
}
