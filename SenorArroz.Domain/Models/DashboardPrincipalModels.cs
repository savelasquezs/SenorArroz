namespace SenorArroz.Domain.Models;

/// <summary>
/// Agregados de pedidos en un rango de fechas (CreatedAt) para KPIs del dashboard principal.
/// </summary>
public record PrincipalKpiSnapshot(
    decimal TotalSalesCop,
    int CompletedOrderCount,
    int AvgTicketCop,
    double CancellationRatePercent);

public record PrincipalPipelineCounts(
    int Taken,
    int InPreparation,
    int Ready,
    int OnTheWay);
