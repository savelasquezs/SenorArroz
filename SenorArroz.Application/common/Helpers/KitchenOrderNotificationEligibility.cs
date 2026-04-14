using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Cocina solo debe recibir avisos ruidosos cuando el pedido está en flujo activo (no reserva pendiente de hora).
/// </summary>
public static class KitchenOrderNotificationEligibility
{
    public static bool IsVisibleToActiveKitchen(Order order, DateTime utcNow)
    {
        if (order.Status is not OrderStatus.Taken and not OrderStatus.InPreparation)
            return false;

        if (order.Type != OrderType.Reservation)
            return true;

        var kitchenEntry = order.PrepareAt
            ?? (order.ReservedFor.HasValue ? order.ReservedFor.Value.AddHours(-1) : (DateTime?)null);

        if (!kitchenEntry.HasValue)
            return false;

        return NormalizeUtc(kitchenEntry.Value) <= NormalizeUtc(utcNow);
    }

    private static DateTime NormalizeUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };
}
