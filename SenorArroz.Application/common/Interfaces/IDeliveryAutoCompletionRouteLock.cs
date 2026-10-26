namespace SenorArroz.Application.Common.Interfaces;

public interface IDeliveryAutoCompletionRouteLock
{
    Task ExecuteDriverAsync(int driverId, Func<CancellationToken, Task> action, CancellationToken ct = default) => action(ct);

    Task ExecuteAsync(
        int routeId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}
