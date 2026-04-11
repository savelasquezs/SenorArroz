using System.Linq;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Ciclo de liquidación dentro del día: abonos y entregas posteriores a la última liquidación.
/// </summary>
public static class DeliverymanSettlementCycleHelper
{
    private const string DeliveredKey = "delivered";

    public static bool TryGetDeliveredAtUtc(Order order, out DateTime deliveredAtUtc)
    {
        deliveredAtUtc = default;
        if (!order.GetStatusTimes().TryGetValue(DeliveredKey, out var dt))
            return false;

        deliveredAtUtc = dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
        return true;
    }

    /// <summary>
    /// Incluye el efectivo del pedido en el ciclo si la entrega cayó en el rango del día
    /// y, si hay última liquidación, estrictamente después de ella.
    /// </summary>
    public static bool IsOrderInSettlementCycle(
        Order order,
        DateTime dayFromUtc,
        DateTime dayToUtc,
        DateTime? lastLiquidationAtUtc,
        bool useSettlementCycle)
    {
        if (!useSettlementCycle || !lastLiquidationAtUtc.HasValue)
        {
            if (!TryGetDeliveredAtUtc(order, out var delivered))
                return false;
            return delivered >= dayFromUtc && delivered <= dayToUtc;
        }

        if (!TryGetDeliveredAtUtc(order, out var deliveredAt))
            return false;
        if (deliveredAt < dayFromUtc || deliveredAt > dayToUtc)
            return false;
        return deliveredAt > lastLiquidationAtUtc.Value;
    }

    /// <summary>
    /// Abono perteneciente al ciclo: dentro del rango del día y, si hay última liquidación, creado después de ella.
    /// </summary>
    public static bool IsAdvanceInSettlementCycle(
        DateTime createdAtUtc,
        DateTime dayFromUtc,
        DateTime dayToUtc,
        DateTime? lastLiquidationAtUtc,
        bool useSettlementCycle)
    {
        var created = createdAtUtc.Kind switch
        {
            DateTimeKind.Utc => createdAtUtc,
            DateTimeKind.Local => DateTime.SpecifyKind(createdAtUtc.ToUniversalTime(), DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)
        };

        if (created < dayFromUtc || created > dayToUtc)
            return false;

        if (!useSettlementCycle || !lastLiquidationAtUtc.HasValue)
            return true;

        return created > lastLiquidationAtUtc.Value;
    }

    public static List<Order> FilterOrdersForCycle(
        IEnumerable<Order> orders,
        DateTime dayFromUtc,
        DateTime dayToUtc,
        DateTime? lastLiquidationAtUtc,
        bool useSettlementCycle) =>
        orders
            .Where(o => IsOrderInSettlementCycle(o, dayFromUtc, dayToUtc, lastLiquidationAtUtc, useSettlementCycle))
            .ToList();

    public static decimal SumCashFromOrders(IEnumerable<Order> orders)
    {
        decimal total = 0;
        foreach (var order in orders)
            total += OrderCashPortionHelper.GetCashPortion(order);

        return total;
    }

    /// <summary>
    /// Une listas de pedidos por Id (un mismo pedido no debería repetirse entre listas).
    /// </summary>
    public static List<Order> UnionOrdersById(IEnumerable<Order> first, IEnumerable<Order> second) =>
        first.Concat(second).GroupBy(o => o.Id).Select(g => g.First()).ToList();
}
