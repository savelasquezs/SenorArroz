namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryStayClassificationService
{
    Task<int> ProcessPendingStaysAsync(CancellationToken cancellationToken = default);
}
