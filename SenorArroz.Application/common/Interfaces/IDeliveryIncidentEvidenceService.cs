namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryIncidentEvidenceService
{
    Task<int> ProcessPendingStaysAsync(CancellationToken cancellationToken = default, int? branchId = null, DateTime? endedSinceUtc = null);
}
