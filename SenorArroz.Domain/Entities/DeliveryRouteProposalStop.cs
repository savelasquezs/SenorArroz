using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class DeliveryRouteProposalStop : BaseEntity
{
    public int DeliveryRoutingPlanId { get; set; }
    public int? DeliveryRouteProposalId { get; set; }
    public int OrderId { get; set; }
    public int? StopSequence { get; set; }
    public DateTime EstimatedReadyAtUtc { get; set; }
    public DateTime? EstimatedArrivalAtUtc { get; set; }
    public int TravelFromPreviousSeconds { get; set; }
    public int ServiceSeconds { get; set; }
    public double BearingFromBranchDegrees { get; set; }
    public bool WasReadyAtGeneration { get; set; }
    public bool IsSuggestedWait { get; set; }
    public string? UnroutedReason { get; set; }

    public virtual DeliveryRoutingPlan DeliveryRoutingPlan { get; set; } = null!;
    public virtual DeliveryRouteProposal? DeliveryRouteProposal { get; set; }
    public virtual Order Order { get; set; } = null!;
}
