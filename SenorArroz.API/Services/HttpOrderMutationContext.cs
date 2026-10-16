using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.API.Services;

public sealed class HttpOrderMutationContext(IHttpContextAccessor http) : IOrderMutationContext
{
    private bool _deferNotifications;
    private readonly List<Func<Task>> _notifications = [];

    public void BeginDeferredNotifications() => _deferNotifications = true;

    public bool TryDeferNotification(Func<Task> callback)
    {
        if (!_deferNotifications) return false;
        _notifications.Add(callback);
        return true;
    }

    public IReadOnlyList<Func<Task>> DrainDeferredNotifications()
    {
        _deferNotifications = false;
        var queued = _notifications.ToArray();
        _notifications.Clear();
        return queued;
    }

    public void ClearDeferredNotifications()
    {
        _deferNotifications = false;
        _notifications.Clear();
    }

    public (int OrderId, long Version)? Expected
    {
        get
        {
            var headers = http.HttpContext?.Request.Headers;
            if (headers is null || (!headers.ContainsKey("X-Order-Id") && !headers.ContainsKey("X-Order-Version"))) return null;
            if (!int.TryParse(headers["X-Order-Id"], out var id) || id <= 0
                || !long.TryParse(headers["X-Order-Version"], out var version) || version <= 0)
                throw new OrderConcurrencyException();
            return (id, version);
        }
    }
    public void Complete(int orderId, long version)
    {
        if (http.HttpContext is not { } context) return;
        context.Response.Headers["X-Order-Id"] = orderId.ToString();
        context.Response.Headers["X-Order-Version"] = version.ToString();
    }
}
