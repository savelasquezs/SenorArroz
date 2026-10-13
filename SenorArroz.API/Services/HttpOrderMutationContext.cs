using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.API.Services;

public sealed class HttpOrderMutationContext(IHttpContextAccessor http) : IOrderMutationContext
{
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
