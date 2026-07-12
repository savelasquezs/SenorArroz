// SenorArroz.Application/Features/Products/DTOs/ProductDetailDto.cs
namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductDetailDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    public int? WeightGrams { get; set; }
    public bool Active { get; set; }
    public int? CommercialProfileId { get; set; }
    public string? CommercialProfileName { get; set; }
    public string? Description { get; set; }
    public string? Ingredients { get; set; }
    public string? PhotoUrl { get; set; }
    public int? ServesPeopleMin { get; set; }
    public int? ServesPeopleMax { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Statistical data
    public int TotalSales { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public int TotalCustomers { get; set; }
    public DateTime? LastSoldAt { get; set; }

    /// <summary>Venta diaria de unidades (últimos 90 días, incluye días en cero). Mismo criterio de día que el dashboard de peso.</summary>
    public List<ProductSalesUnitsEvolutionPointDto> SalesUnitsEvolution { get; set; } = new();
}
