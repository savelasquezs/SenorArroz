namespace SenorArroz.Application.Features.Orders.DTOs;

/// <summary>
/// Barrio distinto donde el domiciliario tuvo pedidos en el criterio de fechas/estado/sucursal del historial.
/// </summary>
public class DeliverymanHistoryNeighborhoodDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
