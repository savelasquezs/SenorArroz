using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class OrderBusinessRulesService : IOrderBusinessRulesService
{
    public bool CanUpdateOrder(Order order, string userRole)
    {
        var role = userRole.ToLower();

        // Pedidos cancelados: solo superadmin
        if (order.Status == OrderStatus.Cancelled)
        {
            return role == "superadmin";
        }

        // Pedidos entregados
        if (order.Status == OrderStatus.Delivered)
        {
            // Solo admin y superadmin pueden modificar pedidos entregados
            if (role != "admin" && role != "superadmin")
                return false;

            // Solo si es el mismo día
            return IsSameDay(order.CreatedAt);
        }

        // Pedidos no entregados: admin, superadmin y cashier pueden modificar
        return role == "admin" || role == "superadmin" || role == "cashier";
    }

    public bool CanUpdateOrderProducts(Order order, string userRole)
    {
        var role = userRole.ToLower();

        // Pedidos cancelados: solo superadmin
        if (order.Status == OrderStatus.Cancelled)
        {
            return role == "superadmin";
        }

        // Pedidos entregados
        if (order.Status == OrderStatus.Delivered)
        {
            // Solo admin y superadmin pueden modificar productos de pedidos entregados
            // Cashier NO puede modificar productos de pedidos entregados
            if (role != "admin" && role != "superadmin")
                return false;

            // Solo si es el mismo día
            return IsSameDay(order.CreatedAt);
        }

        // Pedidos no entregados: admin, superadmin y cashier pueden modificar
        return role == "admin" || role == "superadmin" || role == "cashier";
    }

    public bool CanModifyPayments(Order order, string userRole)
    {
        var role = userRole.ToLower();

        // Mismo día: admin, superadmin y cashier pueden modificar
        if (IsSameDay(order.CreatedAt))
        {
            return role == "admin" || role == "superadmin" || role == "cashier";
        }

        // Días posteriores: solo superadmin
        return role == "superadmin";
    }

    public bool CanChangeStatus(Order order, OrderStatus newStatus, string userRole)
    {
        return IsStatusTransitionValid(order.Status, newStatus, userRole);
    }

    public bool IsStatusTransitionValid(OrderStatus currentStatus, OrderStatus newStatus, string userRole)
    {
        var role = userRole.ToLower();

        // Mismo estado, no hay cambio
        if (currentStatus == newStatus)
            return true;

        // Admin y Superadmin tienen control total
        if (role == "admin" || role == "superadmin")
            return true;

        // Desde Cancelled: solo superadmin puede sacar de cancelado
        if (currentStatus == OrderStatus.Cancelled)
            return false;

        // A Cancelled: solo admin y superadmin
        if (newStatus == OrderStatus.Cancelled)
            return false;

        // Validaciones por rol
        switch (role)
        {
            case "cashier":
                return IsValidCashierTransition(currentStatus, newStatus);

            case "kitchen":
                return IsValidKitchenTransition(currentStatus, newStatus);

            case "deliveryman":
                // Los domiciliarios NO deben usar este endpoint
                return false;

            default:
                return false;
        }
    }

    public bool IsSameDay(DateTime orderCreatedAt)
    {
        return orderCreatedAt.Date == DateTime.UtcNow.Date;
    }

    #region Private Helper Methods

    /// <summary>
    /// Valida transiciones permitidas para cajeros (solo hacia adelante)
    /// </summary>
    private bool IsValidCashierTransition(OrderStatus current, OrderStatus next)
    {
        // Cajero solo puede mover hacia adelante en el flujo
        return (current, next) switch
        {
            (OrderStatus.Taken, OrderStatus.InPreparation) => true,
            (OrderStatus.InPreparation, OrderStatus.Ready) => true,
            (OrderStatus.Ready, OrderStatus.OnTheWay) => true,
            (OrderStatus.OnTheWay, OrderStatus.Delivered) => true,
            _ => false // No puede retroceder ni saltar estados
        };
    }

    /// <summary>
    /// Valida transiciones permitidas para cocina
    /// </summary>
    private bool IsValidKitchenTransition(OrderStatus current, OrderStatus next)
    {
        // Cocina solo puede cambiar estados de preparación
        return (current, next) switch
        {
            (OrderStatus.Taken, OrderStatus.InPreparation) => true,
            (OrderStatus.InPreparation, OrderStatus.Ready) => true,
            _ => false
        };
    }

    #endregion
}

