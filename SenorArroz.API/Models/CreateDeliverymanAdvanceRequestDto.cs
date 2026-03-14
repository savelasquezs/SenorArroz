namespace SenorArroz.API.Models;

/// <summary>
/// DTO para crear abono. El deliverymanId va en la ruta POST /deliverymen/{id}/advances.
/// </summary>
public class CreateDeliverymanAdvanceRequestDto
{
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
