namespace SenorArroz.Domain.Entities;

/// <summary>
/// Punto GPS registrado por un domiciliario durante una ruta activa.
/// </summary>
public class DeliverymanLocation
{
    public long Id { get; set; }
    public int DeliverymanId { get; set; }
    public int? DeliveryRouteId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    /// <summary>Momento exacto de la captura GPS en el dispositivo (UTC).</summary>
    public DateTime RecordedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User Deliveryman { get; set; } = null!;
    public virtual DeliveryRoute? DeliveryRoute { get; set; }
}
