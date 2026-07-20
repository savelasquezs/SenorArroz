namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryTrackingAlertService
{
    Task<int> ProcessAsync(CancellationToken cancellationToken = default);
}
