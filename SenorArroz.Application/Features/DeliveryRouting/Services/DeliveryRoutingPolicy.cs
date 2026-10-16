using System.Linq.Expressions;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.DeliveryRouting.Services;

internal static class DeliveryRoutingPolicy
{
    internal static Expression<Func<Order, bool>> Eligible(int branchId, DateTime now, DeliveryRoutingOptions options)
    {
        var cutoff = now.AddSeconds(Math.Clamp(options.PreparationLookaheadSeconds, 0, 3600));
        return order => order.BranchId == branchId
            && (order.Type == OrderType.Delivery || (order.Type == OrderType.Reservation && order.AddressId.HasValue))
            && (order.Status == OrderStatus.Taken || order.Status == OrderStatus.InPreparation || order.Status == OrderStatus.Ready)
            && order.DeliveryManId == null && order.DeliveryRouteId == null
            && string.IsNullOrEmpty(order.ExternalFulfillmentProvider)
            && (order.Type != OrderType.Reservation || order.Status == OrderStatus.Ready
                || (!order.PrepareAt.HasValue && !order.ReservedFor.HasValue)
                || (order.PrepareAt ?? order.ReservedFor) <= cutoff);
    }

    internal static bool Coordinates(decimal? latitude, decimal? longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
        && !(latitude == 0 && longitude == 0);

    internal static DateTime ReadyAt(Order order,
        IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates, DateTime now,
        DeliveryRoutingOptions options) => order.Status == OrderStatus.Ready ? now
        : estimates.TryGetValue(order.Id, out var estimate) ? estimate.EstimatedReadyAtUtc
        : now.AddSeconds(Math.Max(1, options.PreparationFallbackSeconds));

    // Every item is collected at the depot, not manufactured while travelling.
    internal static int DepartureSeconds(IReadOnlyList<Order> orders,
        IReadOnlyDictionary<int, KitchenPreparationEstimate> estimates, DateTime now,
        int vehicleAvailableAtSeconds, DeliveryRoutingOptions options)
    {
        var readyIn = orders.Where(o => o.Status != OrderStatus.Ready)
            .Select(o => Math.Max(1, (int)Math.Ceiling((ReadyAt(o, estimates, now, options) - now).TotalSeconds)))
            .DefaultIfEmpty(0).Max();
        return Math.Max(Math.Max(0, vehicleAvailableAtSeconds), readyIn);
    }
}
