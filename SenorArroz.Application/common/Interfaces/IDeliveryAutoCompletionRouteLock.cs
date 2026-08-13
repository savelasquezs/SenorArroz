namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryAutoCompletionRouteLock
{
    Task ExecuteAsync(
        int routeId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}
