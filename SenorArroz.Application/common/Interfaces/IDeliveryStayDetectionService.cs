namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryStayDetectionService
{
    Task<int> ProcessPendingSessionsAsync(CancellationToken cancellationToken = default, int? branchId = null, DateTime? endedSinceUtc = null);
    Task<int> ProcessSessionAsync(int workSessionId, CancellationToken cancellationToken = default);
}
