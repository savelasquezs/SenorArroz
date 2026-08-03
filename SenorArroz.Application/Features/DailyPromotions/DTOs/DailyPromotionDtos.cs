namespace SenorArroz.Application.Features.DailyPromotions.DTOs;

public class DailyPromotionProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
}

public class DailyPromotionDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int? CreatedByUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? GiftProductId { get; set; }
    public string? GiftProductName { get; set; }
    public string? GiftProductCategoryName { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public string? DiscountScope { get; set; }
    public List<DailyPromotionProductDto> DiscountProducts { get; set; } = [];
    public int? MinimumOrderValue { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool CanManage { get; set; }
}

public class UpsertDailyPromotionDto
{
    public string Type { get; set; } = string.Empty;
    public int? GiftProductId { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public string? DiscountScope { get; set; }
    public List<int> DiscountProductIds { get; set; } = [];
    public int? MinimumOrderValue { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}
