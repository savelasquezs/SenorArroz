namespace SenorArroz.Application.Features.Dashboard.DTOs;

/// <summary>
/// Respuesta <c>GET /api/dashboard/delivery</c> (rango de fechas obligatorio en query).
/// </summary>
public class DashboardDeliveryResponseDto
{
    public double AvgPrepMinutes { get; set; }
    public double AvgDeliveryMinutes { get; set; }
    public List<DeliverymanEfficiencyApiDto> Deliverymen { get; set; } = new();
    public List<string> EvolutionLabels { get; set; } = new();
    public List<int> EvolutionDeliveries { get; set; } = new();
    public List<int> EvolutionFees { get; set; } = new();
}

public class DeliverymanEfficiencyApiDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveredCount { get; set; }
    public double AvgDeliveryMinutes { get; set; }
    public int DeliveryFeeTotal { get; set; }
}
