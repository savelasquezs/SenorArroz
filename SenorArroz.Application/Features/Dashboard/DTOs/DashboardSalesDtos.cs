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
    public List<SalesRankItemDto> TopByQuantity { get; set; } = new();
    public List<RevenueParticipationSliceDto> ParticipationByRevenue { get; set; } = new();
    public long TotalRevenueCop { get; set; }
    public int TotalQuantity { get; set; }
    /// <summary>
    /// Peso total vendido por categoría (gramos). Solo productos con <c>weight_grams</c> definido.
    /// Futuro: podría cruzarse con gastos por categoría para estimar costo (no implementado).
    /// </summary>
    public List<SalesCategoryWeightItemDto> WeightByCategory { get; set; } = new();
}

/// <summary>Gramos vendidos agregados por categoría de producto.</summary>
public class SalesCategoryWeightItemDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TotalWeightGrams { get; set; }
}

/// <summary>Ítem de ranking (producto o categoría según <see cref="SalesProductsGroupBy"/>).</summary>
public class SalesRankItemDto
{
    public int Id { get; set; }
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

#region Peso por categoría (torta + evolución)

/// <summary>
/// Distribución de peso vendido (g) por categoría en el rango y, si <c>categoryId</c> se envió en la query, serie temporal para esa categoría.
/// </summary>
public class DashboardCategoryWeightsResponseDto
{
    public List<SalesCategoryWeightItemDto> ByCategory { get; set; } = new();
    public List<CategoryWeightEvolutionPointDto> Evolution { get; set; } = new();
}

public class CategoryWeightEvolutionPointDto
{
    public DateTime BucketStartUtc { get; set; }
    public long TotalWeightGrams { get; set; }
}

#endregion
