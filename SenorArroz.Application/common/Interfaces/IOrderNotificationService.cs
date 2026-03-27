using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyNewOrderToKitchen(OrderDto order);
    Task NotifyOrderReadyToDelivery(OrderDto order);
    Task NotifyReservationToKitchen(OrderDto order);
    Task NotifyOrderAssignedToDelivery(OrderDto order);

    /// <summary>
    /// Cocina: pedido modificado (horario, notas, productos) en taken o in_preparation.
    /// <paramref name="modificationKind"/>: schedule | content | multiple
    /// </summary>
    Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind);

    /// <summary>
    /// Domiciliarios de la sucursal: pedido en camino fue modificado.
    /// </summary>
    Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind);
}

