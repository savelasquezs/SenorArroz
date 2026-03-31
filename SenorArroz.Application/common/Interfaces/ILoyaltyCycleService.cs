using SenorArroz.Application.Features.Customers.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface ILoyaltyCycleService
{
    /// <summary>Rellena conteo de entregados y próximo premio (sin columna en cliente; deriva de pedidos).</summary>
    Task ApplyLoyaltyPreviewToCustomerDtoAsync(CustomerDto dto, CancellationToken cancellationToken = default);

    /// <summary>Al pasar a entregado con cliente: asigna paso y snapshot. Idempotente si ya tenía paso.</summary>
    Task OnOrderDeliveredAsync(int orderId, int branchId, int? customerId, CancellationToken cancellationToken = default);

    /// <summary>Si se revierte entregado (p. ej. domiciliario OnTheWay), limpia marca de fidelidad en el pedido.</summary>
    Task OnOrderLeftDeliveredAsync(int orderId, CancellationToken cancellationToken = default);
}
