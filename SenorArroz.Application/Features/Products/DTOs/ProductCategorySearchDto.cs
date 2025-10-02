// SenorArroz.Application/Features/Products/DTOs/ProductCategorySearchDto.cs
namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductCategorySearchDto
{
    public string? Name { get; set; }
    public int? BranchId { get; set; } // Solo usado por superadmin
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string? SortOrder { get; set; } = "asc";
}