// SenorArroz.Application/Features/Products/DTOs/ProductCategorySearchDto.cs
namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductCategorySearchDto
{
    public string? Name { get; set; }
    /// <summary>Filtro opcional por sucursal de la categoría (todas si se omite).</summary>
    public int? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string? SortOrder { get; set; } = "asc";
}