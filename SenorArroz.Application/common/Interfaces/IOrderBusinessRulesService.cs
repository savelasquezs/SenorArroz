using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Interfaces;

public interface IOrderBusinessRulesService
{
    /// <summary>
    /// Verifica si un usuario puede actualizar un pedido basado en su estado y rol
    /// </summary>
    bool CanUpdateOrder(Order order, string userRole);

    /// <summary>
    /// Verifica si un usuario puede modificar los productos de un pedido
    /// </summary>
    bool CanUpdateOrderProducts(Order order, string userRole);

    /// <summary>
    /// Verifica si un usuario puede modificar los pagos de un pedido
    /// </summary>
    bool CanModifyPayments(Order order, string userRole);

    /// <summary>
    /// Verifica si un usuario puede cambiar el estado de un pedido
    /// </summary>
    bool CanChangeStatus(Order order, OrderStatus newStatus, string userRole);

    /// <summary>
    /// Verifica si una transición de estado es válida para un rol específico
    /// </summary>
    bool IsStatusTransitionValid(Order order, OrderStatus newStatus, string userRole);

    /// <summary>
    /// Verifica si el pedido fue creado el mismo día
    /// </summary>
    bool IsSameDay(DateTime orderCreatedAt);
}

