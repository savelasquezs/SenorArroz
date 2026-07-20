namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryStayDetectionService
{
    Task<int> ProcessPendingSessionsAsync(CancellationToken cancellationToken = default);
    Task<int> ProcessSessionAsync(int workSessionId, CancellationToken cancellationToken = default);
}
