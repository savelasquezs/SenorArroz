// SenorArroz.Application/Features/Products/DTOs/ProductCategoryDto.cs
namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductCategoryDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistics
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
}