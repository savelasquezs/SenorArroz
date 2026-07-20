using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Snapshot estable de un hecho de seguimiento que requiere conservar evidencia.
/// Los identificadores de origen no son llaves foraneas para que la limpieza no invalide el incidente.
/// </summary>
public class DeliveryTrackingIncident
{
    public long Id { get; set; }
    public DeliveryTrackingIncidentType IncidentType { get; set; } = DeliveryTrackingIncidentType.Stay;
    public int BranchId { get; set; }
    public int DeliverymanId { get; set; }
    public int WorkSessionId { get; set; }
    public long? DeliveryStayId { get; set; }
    public int? DeliveryRouteId { get; set; }
    public int? OrderId { get; set; }
    public DeliveryStayClassification? StayClassification { get; set; }
    public string? ClassificationReason { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public decimal CenterLatitude { get; set; }
    public decimal CenterLongitude { get; set; }
    public double RadiusMeters { get; set; }
    public double AverageAccuracyMeters { get; set; }
    public double? DistanceToBranchMeters { get; set; }
    public double? DistanceToOrderMeters { get; set; }
    public string? OrderAddressSnapshot { get; set; }
    public decimal? OrderLatitudeSnapshot { get; set; }
    public decimal? OrderLongitudeSnapshot { get; set; }
    public string? OrderStatusSnapshot { get; set; }
    public DateTime SourceUpdatedAt { get; set; }
    public DateTime EvidenceCapturedAt { get; set; }
    public bool EvidenceComplete { get; set; }
    public DeliveryIncidentReviewStatus ReviewStatus { get; set; } = DeliveryIncidentReviewStatus.Pending;
    public DeliveryStayClassification? FinalClassification { get; set; }
    public string? AdminNotes { get; set; }
    public string? DeliverymanExplanation { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<DeliveryIncidentLocationEvidence> LocationEvidence { get; set; } =
        new List<DeliveryIncidentLocationEvidence>();
    public virtual ICollection<DeliveryIncidentDeviceEventEvidence> DeviceEventEvidence { get; set; } =
        new List<DeliveryIncidentDeviceEventEvidence>();
}
