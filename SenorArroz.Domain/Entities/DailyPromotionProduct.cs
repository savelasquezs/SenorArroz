using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class DailyPromotionProduct : BaseEntity
{
    public int DailyPromotionId { get; set; }
    public int ProductId { get; set; }

    public virtual DailyPromotion DailyPromotion { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}
