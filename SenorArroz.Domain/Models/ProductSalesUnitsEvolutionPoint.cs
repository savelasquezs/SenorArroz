namespace SenorArroz.Domain.Models;

/// <summary>Punto diario de unidades vendidas de un producto (solo pedidos no cancelados).</summary>
public sealed record ProductSalesUnitsEvolutionPoint(DateTime BucketDate, int UnitsSold);
