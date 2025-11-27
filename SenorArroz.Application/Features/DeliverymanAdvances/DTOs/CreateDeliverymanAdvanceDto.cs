namespace SenorArroz.Application.Features.DeliverymanAdvances.DTOs;

public class CreateDeliverymanAdvanceDto
{
    public int DeliverymanId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

