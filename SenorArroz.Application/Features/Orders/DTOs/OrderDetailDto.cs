namespace SenorArroz.Application.Features.Orders.DTOs;

public class OrderDetailDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductDescription { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int Discount { get; set; }
    public int? Subtotal { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}