using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DeliveryRoutingPlan : BaseEntity
{
    public int BranchId { get; set; }
    public long GenerationNumber { get; set; }
    public DeliveryRoutingPlanStatus Status { get; set; } = DeliveryRoutingPlanStatus.Active;
    public DateTime GeneratedAtUtc { get; set; }
    public string InputFingerprint { get; set; } = string.Empty;
    public int AvailableSlotCount { get; set; }
    public int SoonSlotCount { get; set; }
    public int SolverDurationMs { get; set; }
    public RoutingMatrixSource MatrixSource { get; set; } = RoutingMatrixSource.Approximate;
    public string? Warnings { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<DeliveryRouteProposal> Proposals { get; set; } = new List<DeliveryRouteProposal>();
    public virtual ICollection<DeliveryRouteProposalStop> Stops { get; set; } = new List<DeliveryRouteProposalStop>();
}
