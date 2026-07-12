namespace SenorArroz.Application.Features.Products.DTOs;

public class ProductDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    /// <summary>Peso unitario en gramos (opcional).</summary>
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
}
