using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryRouteWorkflowService
{
    Task OnOrderAssignedToDeliverymanAsync(Order order, CancellationToken cancellationToken = default);

    Task OnOrderUnassignedAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Solo efecto si la ruta sigue en Open (retira parada y limpia FK).</summary>
    Task OnOrderCancelledWhileRouteOpenAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Si el pedido pertenece a una ruta InProgress y todos los pedidos están terminal, cierra métricas.</summary>
    Task TryCompleteInProgressRouteAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Consolida rutas abiertas cuya última asignación superó el delay. Ejecutado por worker.</summary>
    Task<int> ConsolidatePendingRoutesAsync(CancellationToken cancellationToken = default);
}
