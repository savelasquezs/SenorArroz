using System.Security.Claims;
using SenorArroz.API.Infrastructure;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.API.Middleware;

public sealed class DeliveryAppVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IDeliveryAppVersionPolicy versionPolicy,
        IAuthRepository authRepository)
    {
        var isDeliveryman = context.User.Identity?.IsAuthenticated == true
            && context.User.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && string.Equals(
                    claim.Value,
                    "Deliveryman",
                    StringComparison.OrdinalIgnoreCase));

        if (isDeliveryman)
        {
            if (DeliveryAppVersionHeaders.IsWebClient(context.Request))
            {
                var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdValue, out var userId)
                    || !await authRepository.CanDeliverymanAccessWebAsync(userId, context.RequestAborted))
                {
                    throw new DeliverymanWebAccessDeniedException();
                }
            }
            else
            {
                versionPolicy.EnsureCompatible(DeliveryAppVersionHeaders.Read(context.Request));
            }
        }

        await next(context);
    }
}
