namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Optional optimistic concurrency precondition from an interactive client.</summary>
public interface IOrderMutationContext
{
    (int OrderId, long Version)? Expected { get; }
    void Complete(int orderId, long version);

    // Defaults preserve non-HTTP/mock mutation contexts.
    bool IsDeferringNotifications => false;
    void BeginDeferredNotifications() { }
    bool TryDeferNotification(Func<Task> callback) => false;
    IReadOnlyList<Func<Task>> DrainDeferredNotifications() => [];
    void ClearDeferredNotifications() { }
}
