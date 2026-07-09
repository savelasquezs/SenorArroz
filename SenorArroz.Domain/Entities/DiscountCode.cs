using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DiscountCode : BaseEntity
{
    public int BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public LoyaltyRewardType Type { get; set; }
    public int? GiftProductId { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? MinimumOrderValue { get; set; }
    public bool IsActive { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Product? GiftProduct { get; set; }
}
