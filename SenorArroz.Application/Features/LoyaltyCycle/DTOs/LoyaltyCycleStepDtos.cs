namespace SenorArroz.Application.Features.LoyaltyCycle.DTOs;

public class LoyaltyCycleStepDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int StepIndex { get; set; }
    public string? StepName { get; set; }
    public string RewardLabel { get; set; } = string.Empty;
    public string? RewardType { get; set; }
    public int? GiftProductId { get; set; }
    public string? GiftProductName { get; set; }
    public string? GiftProductCategoryName { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertLoyaltyCycleStepDto
{
    public int StepIndex { get; set; }
    public string? StepName { get; set; }
    public string RewardLabel { get; set; } = string.Empty;
    public string RewardType { get; set; } = string.Empty;
    public int? GiftProductId { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public bool IsActive { get; set; } = true;
}
