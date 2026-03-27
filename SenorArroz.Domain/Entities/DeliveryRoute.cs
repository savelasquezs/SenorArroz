using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Ruta de entrega de un domiciliario: métricas planeadas (Google + buffers) vs tiempo real.
/// </summary>
public class DeliveryRoute : BaseEntity
{
    public int DeliverymanId { get; set; }
    public int BranchId { get; set; }
    public DeliveryRouteStatus Status { get; set; } = DeliveryRouteStatus.Open;

    /// <summary>UTC: momento de la última asignación de un pedido a esta ruta (reinicia ventana de consolidación).</summary>
    public DateTime LastAssignmentAtUtc { get; set; }

    /// <summary>UTC: inicio del reloj operativo = última asignación + delay de consolidación.</summary>
    public DateTime? RouteStartedAtUtc { get; set; }

    public int? PlannedDistanceMeters { get; set; }
    /// <summary>Distancia recta aprox. última entrega → sucursal al cerrar la ruta (metros).</summary>
    public int? ReturnToBranchMeters { get; set; }
    public int? PlannedDrivingDurationSeconds { get; set; }

    public int StopCount { get; set; }
    public int ComplexAccessStopCount { get; set; }

    /// <summary>Segundos sumados por pedido (4 min por defecto).</summary>
    public int PerOrderBufferSeconds { get; set; } = 240;

    /// <summary>Segundos por parada con acceso complejo (8 min por defecto en opciones).</summary>
    public int ComplexAccessBufferSeconds { get; set; } = 480;

    /// <summary>
    /// Meta total en segundos: tiempo manejando Google + N×per_order + K×complex.
    /// </summary>
    public int? MetaDurationSeconds { get; set; }

    public DateTime? ConsolidatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? ActualDurationSeconds { get; set; }
    public bool? MetSla { get; set; }

    /// <summary>Advertencias al consolidar (saltos de línea). Null si el plan usó Google sin incidencias.</summary>
    public string? PlanningWarnings { get; set; }

    public virtual User Deliveryman { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<DeliveryRouteStop> Stops { get; set; } = new List<DeliveryRouteStop>();
}
