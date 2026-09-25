using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class Product : TenantOwnedEntity
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    /// <summary>Peso unitario en gramos (opcional). Usado en dashboard de ventas (peso por categoría).</summary>
    public int? WeightGrams { get; set; }
    public bool Active { get; set; } = true;
    public int? CommercialProfileId { get; set; }
    public int? ServesPeopleMin { get; set; }
    public int? ServesPeopleMax { get; set; }
    public string? StorefrontVariantLabel { get; set; }
    public int StorefrontSortOrder { get; set; }
    public bool InventoryEnabled { get; set; }
    public InventoryControlMode InventoryControlMode { get; set; } = InventoryControlMode.Estimated;

    // Navigation Properties
    public virtual ProductCategory Category { get; set; } = null!;
    public virtual CommercialProfile? CommercialProfile { get; set; }
    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public virtual ICollection<DailyPromotion> GiftDailyPromotions { get; set; } = new List<DailyPromotion>();
    public virtual ICollection<DailyPromotionProduct> DailyPromotionProducts { get; set; } = new List<DailyPromotionProduct>();
    public virtual ICollection<LoyaltyCycleStep> LoyaltyGiftSteps { get; set; } = new List<LoyaltyCycleStep>();
    public virtual ICollection<DiscountCode> GiftDiscountCodes { get; set; } = new List<DiscountCode>();
    public virtual ICollection<ProductExpenseRequirement> InventoryRequirements { get; set; } = new List<ProductExpenseRequirement>();
}
