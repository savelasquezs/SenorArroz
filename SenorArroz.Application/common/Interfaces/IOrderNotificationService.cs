using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyNewOrderToKitchen(OrderDto order);
    Task NotifyOrderReadyToDelivery(OrderDto order);
    Task NotifyReservationToKitchen(OrderDto order);
    Task NotifyOrderAssignedToDelivery(OrderDto order);
    Task NotifyDeliveryRoutingPlanChanged(int branchId, int planId, long version) => Task.CompletedTask;
    Task NotifyRouteProposalClaimed(int branchId, int proposalId, int deliverymanId) => Task.CompletedTask;

    /// <summary>
    /// Cocina: pedido modificado (horario, notas, productos) en taken o in_preparation.
    /// <paramref name="modificationKind"/>: schedule | content | multiple
    /// </summary>
    Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null);

    /// <summary>
    /// Domiciliarios de la sucursal: pedido en camino fue modificado.
    /// </summary>
    Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null);

    /// <summary>Cocina: pedido cancelado mientras estaba en flujo activo.</summary>
    Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null);

    /// <summary>
    /// Admins de la sucursal: actualización de ubicación GPS del domiciliario.
    /// </summary>
    Task NotifyDeliverymanLocation(
        int branchId,
        int deliverymanId,
        int? deliveryRouteId,
        double latitude,
        double longitude,
        DateTime recordedAt);
}

