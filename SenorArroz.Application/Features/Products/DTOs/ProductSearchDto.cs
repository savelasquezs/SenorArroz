// SenorArroz.Application/Features/Products/DTOs/ProductSearchDto.cs
namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductSearchDto
{
    public string? Name { get; set; }
    public int? CategoryId { get; set; }
    public int? BranchId { get; set; } // Solo usado por superadmin
    public bool? Active { get; set; }
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string? SortOrder { get; set; } = "asc";
}