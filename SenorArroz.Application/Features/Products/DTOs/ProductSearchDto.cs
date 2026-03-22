// SenorArroz.Application/Features/Products/DTOs/ProductSearchDto.cs
namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductSearchDto
{
    public string? Name { get; set; }
    public int? CategoryId { get; set; }
    /// <summary>Filtro opcional por sucursal de la categoría del producto (todas si se omite).</summary>
    public int? BranchId { get; set; }
    public bool? Active { get; set; }
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; } = "name";
    public string? SortOrder { get; set; } = "asc";
}