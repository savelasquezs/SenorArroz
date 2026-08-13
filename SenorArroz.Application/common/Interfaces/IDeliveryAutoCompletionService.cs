using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryAutoCompletionService
{
    Task EvaluateLocationAsync(
        DeliverymanLocation location,
        CancellationToken cancellationToken = default);
}
