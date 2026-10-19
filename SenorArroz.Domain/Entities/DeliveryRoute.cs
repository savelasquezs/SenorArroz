using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

/// <summary>Delivery trip, with assignment, observed departure and return kept distinct.</summary>
public class DeliveryRoute : TenantOwnedEntity
{
    public int DeliverymanId { get; set; }
    public int BranchId { get; set; }
    public DeliveryRouteStatus Status { get; set; } = DeliveryRouteStatus.Open;

    /// <summary>Null only for historical/unbound trips. Never reassigned to another workday.</summary>
    public int? WorkSessionId { get; set; }
    /// <summary>Actual departure evidence; assignment/consolidation is not a physical departure.</summary>
    public DateTime? DepartedAtUtc { get; set; }
    /// <summary>gps or confirmed; null means departure has not been observed.</summary>
    public string? DepartureSource { get; set; }

    /// <summary>Last assignment, used only by the consolidation window.</summary>
    public DateTime LastAssignmentAtUtc { get; set; }
    /// <summary>Legacy operational clock (assignment + consolidation), not physical departure.</summary>
    public DateTime? RouteStartedAtUtc { get; set; }
    public int? PlannedDistanceMeters { get; set; }
    public int? ReturnToBranchMeters { get; set; }
    public int? PlannedDrivingDurationSeconds { get; set; }
    public int StopCount { get; set; }
    public int ComplexAccessStopCount { get; set; }
    public int PerOrderBufferSeconds { get; set; } = 240;
    public int ComplexAccessBufferSeconds { get; set; } = 480;
    public int? MetaDurationSeconds { get; set; }
    public DateTime? ConsolidatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? ActualDurationSeconds { get; set; }
    public bool? MetSla { get; set; }
    public string? PlanningWarnings { get; set; }
    public virtual User Deliveryman { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<DeliveryRouteStop> Stops { get; set; } = new List<DeliveryRouteStop>();
}
