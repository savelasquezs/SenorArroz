using System.Text.Json.Serialization;

namespace SenorArroz.Application.Features.Dashboard.DTOs;

public class DashboardMainResponseDto
{
    public DashboardKpiDto Kpis { get; set; } = null!;
    public DashboardPipelineDto Pipeline { get; set; } = null!;
    public List<DashboardActivityItemDto> RecentActivity { get; set; } = new();

    /// <summary>
    /// Promedio minutos preparación (tomado/inicio prep → listo) sobre pedidos domicilio entregados en la misma ventana que los KPI mostrados.
    /// </summary>
    public double AvgPrepMinutes { get; set; }

    /// <summary>
    /// Promedio minutos entrega (listo → entregado) en esa ventana.
    /// </summary>
    public double AvgDeliveryMinutes { get; set; }
}

public class DashboardKpiDto
{
    public int TotalSales { get; set; }
    public double TotalSalesWeekChangePercent { get; set; }
    public double TotalSalesYearChangePercent { get; set; }

    public int OrdersCount { get; set; }
    public double OrdersWeekChangePercent { get; set; }
    public double OrdersYearChangePercent { get; set; }

    public int AvgTicket { get; set; }
    public double AvgTicketWeekChangePercent { get; set; }
    public double AvgTicketYearChangePercent { get; set; }

    public double CancellationRate { get; set; }
    public double CancellationRateWeekChangePercent { get; set; }
    public double CancellationRateYearChangePercent { get; set; }
}

/// <summary>
/// Nombres JSON alineados al front (snake_case en propiedades del contrato de gráficos).
/// </summary>
public class DashboardPipelineDto
{
    [JsonPropertyName("taken")]
    public int Taken { get; set; }

    [JsonPropertyName("in_preparation")]
    public int InPreparation { get; set; }

    [JsonPropertyName("ready")]
    public int Ready { get; set; }

    [JsonPropertyName("on_the_way")]
    public int OnTheWay { get; set; }
}

public class DashboardActivityItemDto
{
    public int Id { get; set; }
    public string Type { get; set; } = "order";
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Branch { get; set; } = string.Empty;
    public int BranchId { get; set; }
}
