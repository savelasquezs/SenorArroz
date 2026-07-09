using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class DailyPromotion : BaseEntity
{
    public int BranchId { get; set; }
    public DailyPromotionType Type { get; set; }
    public int? GiftProductId { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public DailyPromotionDiscountScope? DiscountScope { get; set; }
    public int? MinimumOrderValue { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Product? GiftProduct { get; set; }
    public virtual ICollection<DailyPromotionProduct> DiscountProducts { get; set; } = new List<DailyPromotionProduct>();
}
