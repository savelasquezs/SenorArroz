using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryRouteProposal : BaseEntity
{
    public int DeliveryRoutingPlanId { get; set; }
    public int Sequence { get; set; }
    public DeliveryRouteProposalStatus Status { get; set; } = DeliveryRouteProposalStatus.Available;
    public DeliveryRouteRecommendation Recommendation { get; set; }
    public DateTime ExpectedDepartureAtUtc { get; set; }
    public int WaitSeconds { get; set; }
    public int ApproximateDrivingDurationSeconds { get; set; }
    public int ApproximateDistanceMeters { get; set; }
    public int? ValidatedDrivingDurationSeconds { get; set; }
    public int? ValidatedDistanceMeters { get; set; }
    public GoogleRouteValidationStatus GoogleValidationStatus { get; set; } = GoogleRouteValidationStatus.NotRequested;
    public int LastDeliverySeconds { get; set; }
    public int WorstAgeAtDeliverySeconds { get; set; }
    public double DirectionSpreadDegrees { get; set; }
    public long Score { get; set; }
    public int? ClaimedByDeliverymanId { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public string? PlanningWarnings { get; set; }

    public virtual DeliveryRoutingPlan DeliveryRoutingPlan { get; set; } = null!;
    public virtual User? ClaimedByDeliveryman { get; set; }
    public virtual ICollection<DeliveryRouteProposalStop> Stops { get; set; } = new List<DeliveryRouteProposalStop>();
}
