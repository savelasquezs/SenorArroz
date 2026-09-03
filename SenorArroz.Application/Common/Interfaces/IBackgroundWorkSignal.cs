namespace SenorArroz.Application.Common.Interfaces;

public interface IBackgroundWorkSignal<TWork> where TWork : class
{
    void Pulse();
    ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class RappiWork;
public sealed class EmailOutboxWork;
public sealed class PaymentNotificationOutboxWork;
public sealed class DeliveryRouteConsolidationWork;
public sealed class DeliveryWorkSessionScheduleWork;
