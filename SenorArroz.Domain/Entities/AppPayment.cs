using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class AppPayment : BaseEntity
{
    public int OrderId { get; set; }
    public int AppId { get; set; }
    public decimal Amount { get; set; }
    public decimal? EstimatedCommissionRate { get; set; }
    public decimal? EstimatedCommissionAmount { get; set; }
    public decimal? ExpectedNetAmount { get; set; }
    public decimal? ActualSettledAmount { get; set; }
    public decimal? SettlementVariance { get; set; }
    public bool IsSetted { get; set; } = false;
    public bool IsReversed { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string? ReversalReason { get; set; }

    // Navigation Properties
    public virtual Order Order { get; set; } = null!;
    public virtual App App { get; set; } = null!;
}
