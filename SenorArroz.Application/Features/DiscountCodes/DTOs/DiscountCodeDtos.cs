namespace SenorArroz.Application.Features.DiscountCodes.DTOs;

public class DiscountCodeDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? GiftProductId { get; set; }
    public string? GiftProductName { get; set; }
    public string? GiftProductCategoryName { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? MinimumOrderValue { get; set; }
    public bool IsActive { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpsertDiscountCodeDto
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? GiftProductId { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? MinimumOrderValue { get; set; }
    public bool IsActive { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}
