using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Permanencia detectada a partir de puntos GPS consecutivos de una jornada.
/// Su clasificación operativa se realiza en una etapa posterior.
/// </summary>
public class DeliveryStay
{
    public long Id { get; set; }
    public int DeliverymanId { get; set; }
    public int WorkSessionId { get; set; }
    public int? DeliveryRouteId { get; set; }
    public int? NearestOrderId { get; set; }
    public int? AuthorizedPlaceId { get; set; }
    public long FirstLocationId { get; set; }
    public long LastLocationId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public decimal CenterLatitude { get; set; }
    public decimal CenterLongitude { get; set; }
    public double RadiusMeters { get; set; }
    public double AverageAccuracyMeters { get; set; }
    public double? DistanceToBranchMeters { get; set; }
    public double? DistanceToNearestOrderMeters { get; set; }
    public double? DistanceToAuthorizedPlaceMeters { get; set; }
    public int PointCount { get; set; }
    public DeliveryStayClassification Classification { get; set; } = DeliveryStayClassification.PendingReview;
    public string? ClassificationReason { get; set; }
    public DateTime? ClassifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User Deliveryman { get; set; } = null!;
    public virtual DeliveryWorkSession WorkSession { get; set; } = null!;
    public virtual DeliveryRoute? DeliveryRoute { get; set; }
    public virtual Order? NearestOrder { get; set; }
    public virtual DeliveryAuthorizedPlace? AuthorizedPlace { get; set; }

    public void InvalidateClassification()
    {
        Classification = DeliveryStayClassification.PendingReview;
        ClassificationReason = "awaiting_classification";
        ClassifiedAt = null;
        AuthorizedPlaceId = null;
        DistanceToAuthorizedPlaceMeters = null;
    }
}
