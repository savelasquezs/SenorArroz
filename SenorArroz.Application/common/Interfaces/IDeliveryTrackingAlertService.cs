namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryTrackingAlertService
{
    Task<int> ProcessAsync(CancellationToken cancellationToken = default);
    Task<int> ProcessPendingStayAlertsAsync(CancellationToken cancellationToken = default, int? branchId = null, DateTime? endedSinceUtc = null);
}
