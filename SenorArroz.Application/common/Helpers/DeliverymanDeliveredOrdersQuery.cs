using System.Linq;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>
/// Carga pedidos entregados cuyo instante <c>delivered</c> en <c>status_times</c> cae en el rango UTC,
/// paginando en el repositorio (mismo límite por página que antes: 500).
/// </summary>
public static class DeliverymanDeliveredOrdersQuery
{
    private const int PageSize = 500;
    private const int MaxPages = 100;

    public static async Task<List<Order>> LoadAllDeliveredInRangeAsync(
        IOrderRepository orderRepository,
        int? branchId,
        int? deliveryManId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var delivery = await LoadForTypeAsync(
            orderRepository, branchId, deliveryManId, OrderType.Delivery, fromUtc, toUtc, cancellationToken);
        var onsite = await LoadForTypeAsync(
            orderRepository, branchId, deliveryManId, OrderType.Onsite, fromUtc, toUtc, cancellationToken);
        return DeliverymanSettlementCycleHelper.UnionOrdersById(delivery, onsite);
    }

    private static async Task<List<Order>> LoadForTypeAsync(
        IOrderRepository orderRepository,
        int? branchId,
        int? deliveryManId,
        OrderType type,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var list = new List<Order>();
        var page = 1;
        while (page <= MaxPages)
        {
            var batch = await orderRepository.SearchDeliveredOrdersByDeliveredAtRangeAsync(
                branchId,
                deliveryManId,
                type,
                fromUtc,
                toUtc,
                page,
                PageSize,
                cancellationToken);

            var pageItems = batch.Items.ToList();
            list.AddRange(pageItems);
            if (pageItems.Count < PageSize || list.Count >= batch.TotalCount)
                break;
            page++;
        }

        return list;
    }
}
