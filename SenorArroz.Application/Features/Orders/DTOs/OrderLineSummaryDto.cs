namespace SenorArroz.Application.Features.Orders.DTOs;

/// <summary>
/// Resumen de línea para listados de pedidos (nombre + cantidad).
/// </summary>
public class OrderLineSummaryDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
