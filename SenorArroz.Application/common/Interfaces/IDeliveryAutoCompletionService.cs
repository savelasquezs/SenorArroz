using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public sealed record DeliveryReturnReview(int RouteId, long ReturnLocationId, IReadOnlyList<int> PendingOrderIds);

public interface IDeliveryAutoCompletionService
{
    Task EvaluateLocationAsync(
        DeliverymanLocation location,
        CancellationToken cancellationToken = default);

    Task<DeliveryReturnReview?> ReconcileReturnAsync(
        DeliverymanLocation location, CancellationToken cancellationToken = default);

    Task ConfirmReturnedOrdersAsync(
        int routeId, int deliverymanId, int branchId, long returnLocationId,
        IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default);
}
