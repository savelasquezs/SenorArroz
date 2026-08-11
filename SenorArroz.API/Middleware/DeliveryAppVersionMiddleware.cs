using System.Security.Claims;
using SenorArroz.API.Infrastructure;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Middleware;

public sealed class DeliveryAppVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IDeliveryAppVersionPolicy versionPolicy)
    {
        var isDeliveryman = context.User.Identity?.IsAuthenticated == true
            && context.User.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && string.Equals(
                    claim.Value,
                    "Deliveryman",
                    StringComparison.OrdinalIgnoreCase));

        if (isDeliveryman && !DeliveryAppVersionHeaders.IsWebClient(context.Request))
            versionPolicy.EnsureCompatible(DeliveryAppVersionHeaders.Read(context.Request));

        await next(context);
    }
}
