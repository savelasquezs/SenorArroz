using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class OrderBusinessRulesService : IOrderBusinessRulesService
{
    private readonly IClock _clock;

    public OrderBusinessRulesService(IClock clock)
    {
        _clock = clock;
    }

    public bool CanUpdateOrder(Order order, string userRole)
    {
        if (order.Status == OrderStatus.Cancelled)
            return Roles.IsSuperadmin(userRole);

        if (order.Status == OrderStatus.Delivered)
        {
            if (!Roles.IsAdminOrSuperadmin(userRole))
                return false;

            return IsSameDay(order.CreatedAt);
        }

        return Roles.IsSuperadminOrAdminOrCashier(userRole);
    }

    public bool CanUpdateOrderProducts(Order order, string userRole)
    {
        if (order.Status == OrderStatus.Cancelled)
            return Roles.IsSuperadmin(userRole);

        if (order.Status == OrderStatus.Delivered)
        {
            if (!Roles.IsAdminOrSuperadmin(userRole))
                return false;

            return IsSameDay(order.CreatedAt);
        }

        return Roles.IsSuperadminOrAdminOrCashier(userRole);
    }

    public bool CanModifyPayments(Order order, string userRole)
    {
        if (IsSameDay(order.CreatedAt)
            || (order.PrepareAt.HasValue && IsSameDay(order.PrepareAt.Value)))
            return Roles.IsSuperadminOrAdminOrCashier(userRole);

        return Roles.IsSuperadmin(userRole);
    }

    public bool CanChangeStatus(Order order, OrderStatus newStatus, string userRole)
    {
        return IsStatusTransitionValid(order, newStatus, userRole);
    }

    public bool IsStatusTransitionValid(Order order, OrderStatus newStatus, string userRole)
    {
        if (order.Status == newStatus)
            return true;

        if (order.Status == OrderStatus.Cancelled)
            return Roles.IsAdminOrSuperadmin(userRole) && newStatus == OrderStatus.Ready;

        if (Roles.IsAdminOrSuperadmin(userRole))
            return true;

        if (newStatus == OrderStatus.Cancelled)
            return false;

        var role = userRole.ToLowerInvariant();
        return role switch
        {
            Roles.Cashier => IsValidCashierTransition(order, newStatus),
            Roles.Kitchen => IsValidKitchenTransition(order.Status, newStatus),
            Roles.Deliveryman => IsValidDeliverymanTransition(order.Status, newStatus),
            _ => false,
        };
    }

    public bool IsSameDay(DateTime orderCreatedAt) =>
        ColombiaTimeHelper.IsColombiaTodayFromUtc(orderCreatedAt, _clock.UtcNow);

    #region Private Helper Methods

    private bool IsValidCashierTransition(Order order, OrderStatus next)
    {
        var current = order.Status;

        return (current, next) switch
        {
            (OrderStatus.Taken, OrderStatus.InPreparation) => true,
            (OrderStatus.InPreparation, OrderStatus.Ready) => true,
            (OrderStatus.Ready, OrderStatus.OnTheWay) => true,
            (OrderStatus.Ready, OrderStatus.Delivered) => order.Type == OrderType.Onsite,
            (OrderStatus.OnTheWay, OrderStatus.Delivered) => true,
            _ => false
        };
    }

    private bool IsValidKitchenTransition(OrderStatus current, OrderStatus next)
    {
        return (current, next) switch
        {
            (OrderStatus.Taken, OrderStatus.InPreparation) => true,
            (OrderStatus.InPreparation, OrderStatus.Ready) => true,
            _ => false
        };
    }

    private bool IsValidDeliverymanTransition(OrderStatus current, OrderStatus next)
    {
        return (current, next) switch
        {
            (OrderStatus.OnTheWay, OrderStatus.Delivered) => true,
            (OrderStatus.Delivered, OrderStatus.OnTheWay) => true,
            _ => false
        };
    }

    #endregion
}
