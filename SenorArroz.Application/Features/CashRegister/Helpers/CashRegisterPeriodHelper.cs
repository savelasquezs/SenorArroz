using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

/// <summary>
/// Instante contable de venta para el cuadre: no usa <see cref="BaseEntity.UpdatedAt"/> (cambios posteriores al pedido moverían el período).
/// </summary>
public static class CashRegisterPeriodHelper
{
    /// <summary>
    /// <see cref="Order.PrepareAt"/> si existe; si no <see cref="BaseEntity.CreatedAt"/>. Ambos normalizados a UTC.
    /// </summary>
    public static DateTime GetEffectiveDeliveredSalesInstantUtc(Order order)
    {
        var instant = order.PrepareAt ?? order.CreatedAt;
        return ColombiaTimeHelper.EnsureUtc(instant);
    }

    public static bool IsDeliveredSaleInCashRegisterPeriod(Order order, DateTime sinceUtcInclusiveExclusive, DateTime nowUtcInclusive)
    {
        if (order.Status != OrderStatus.Delivered)
            return false;

        var t = GetEffectiveDeliveredSalesInstantUtc(order);
        return t > sinceUtcInclusiveExclusive && t <= nowUtcInclusive;
    }

    public static bool IsDeliveredSaleInCashRegisterPeriod(
        OrderStatus status,
        DateTime? prepareAt,
        DateTime createdAt,
        DateTime sinceUtcInclusiveExclusive,
        DateTime nowUtcInclusive)
    {
        if (status != OrderStatus.Delivered)
            return false;

        var instant = prepareAt ?? createdAt;
        var t = ColombiaTimeHelper.EnsureUtc(instant);
        return t > sinceUtcInclusiveExclusive && t <= nowUtcInclusive;
    }
}
