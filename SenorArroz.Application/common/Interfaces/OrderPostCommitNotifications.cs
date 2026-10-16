namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Publish now unless an order mutation is still awaiting commit.</summary>
public static class OrderPostCommitNotifications
{
    public static Task Publish(IOrderMutationContext? context, Func<Task> callback) =>
        context?.TryDeferNotification(callback) == true ? Task.CompletedTask : callback();
}
