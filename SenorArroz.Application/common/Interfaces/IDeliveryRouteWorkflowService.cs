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

    /// <summary>
    /// Si todos los pedidos de la ruta son terminales: cierra Open con meta completa (actual=meta) o completa InProgress.
    /// <paramref name="routeIdIfOrderUnlinked"/> cuando el pedido ya no tiene FK (ej. cancelado en ruta Open).
    /// </summary>
    Task TryFinalizeRouteWhenAllTerminalAsync(int orderId, int? routeIdIfOrderUnlinked = null, CancellationToken cancellationToken = default);

    /// <summary>True si hay ruta Open/InProgress con pedido no terminal. <paramref name="excludeOrderIds"/> excluye IDs (mismo lote de autoasignación).</summary>
    Task<bool> DeliverymanHasPendingOrdersOnActiveRouteAsync(
        int deliverymanId,
        int branchId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? excludeOrderIds = null);

    /// <summary>Consolida rutas abiertas cuya última asignación superó el delay. Ejecutado por worker.</summary>
    Task<int> ConsolidatePendingRoutesAsync(CancellationToken cancellationToken = default);

    Task<DateTime?> GetNextPendingConsolidationAtAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTime?>(null);
}
