namespace SenorArroz.Application.Features.Dashboard.DTOs;

#region Comparación sucursales

public class DashboardSalesComparisonResponseDto
{
    public List<DashboardSalesComparisonRowDto> Rows { get; set; } = new();
}

public class DashboardSalesComparisonRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SalesTotal { get; set; }
    public int OrdersTotal { get; set; }
    public int SalesDelivery { get; set; }
    public int SalesOnsite { get; set; }
    public int OrdersDelivery { get; set; }
    public int OrdersOnsite { get; set; }
    /// <summary>Reservado; hoy 0 (futuro: SLA domicilio por sucursal).</summary>
    public int DeliveryTimeMinutes { get; set; }
}

#endregion

#region Evolución temporal (TimeEvolutionPanel)

public class DashboardSalesEvolutionResponseDto
{
    public SalesTimeSeriesBlockDto SalesByDay { get; set; } = null!;
    public SalesTimeSeriesBlockDto SalesByHour { get; set; } = null!;
    public SalesTimeSeriesBlockDto SalesByMonth { get; set; } = null!;
    public SalesTimeSeriesBlockDto SalesByYear { get; set; } = null!;
    public OrdersTimelineBlockDto OrdersByDay { get; set; } = null!;
    public OrdersTimelineBlockDto OrdersByHour { get; set; } = null!;
    public OrdersTimelineBlockDto OrdersByMonth { get; set; } = null!;
    public OrdersTimelineBlockDto OrdersByYear { get; set; } = null!;
}

public class SalesTimeSeriesBlockDto
{
    public List<string> Labels { get; set; } = new();
    public List<SalesSeriesDatasetDto> Datasets { get; set; } = new();
}

public class SalesSeriesDatasetDto
{
    public string Label { get; set; } = string.Empty;
    public List<int> Data { get; set; } = new();
}

public class OrdersTimelineBlockDto
{
    public List<string> Labels { get; set; } = new();
    public List<int> Counts { get; set; } = new();
}

#endregion

#region Productos

public class DashboardSalesProductsResponseDto
{
    public List<SalesProductRankDto> TopByQuantity { get; set; } = new();
    public List<RevenueParticipationSliceDto> ParticipationByRevenue { get; set; } = new();
    public long TotalRevenueCop { get; set; }
    public int TotalQuantity { get; set; }
}

public class SalesProductRankDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public long RevenueCop { get; set; }
}

public class RevenueParticipationSliceDto
{
    public string Label { get; set; } = string.Empty;
    public double Percent { get; set; }
    public long RevenueCop { get; set; }
}

#endregion
