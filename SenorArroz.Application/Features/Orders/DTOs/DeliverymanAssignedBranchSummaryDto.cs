namespace SenorArroz.Application.Features.Orders.DTOs;

public class DeliverymanAssignedBranchSummaryDto
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    /// <summary>Pedidos en el criterio (p. ej. entregados).</summary>
    public int OrderCount { get; set; }
    /// <summary>Alias para clientes que esperan <c>deliveredCount</c>.</summary>
    public int DeliveredCount { get; set; }
    /// <summary>Suma de <c>DeliveryFee</c> de esos pedidos (COP).</summary>
    public decimal TotalDeliveryFee { get; set; }
    /// <summary>Parte del domiciliario según <c>DeliveryFeePayRate</c> (COP).</summary>
    public decimal PayableDeliveryFee { get; set; }
}
