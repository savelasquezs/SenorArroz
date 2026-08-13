using Microsoft.AspNetCore.Authorization;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Middleware;

public sealed class PublicAssociationScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantExecutionContext executionContext)
    {
        var isExplicitPublicEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var isPrintAgentHub = context.Request.Path.StartsWithSegments("/hubs/print-agent");
        if (!isExplicitPublicEndpoint && !isPrintAgentHub)
        {
            await next(context);
            return;
        }

        using var scope = executionContext.BeginSystemScope();
        await next(context);
    }
}
