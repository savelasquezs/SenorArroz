using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Middleware;

public sealed class TenantCapabilityMiddleware(RequestDelegate next)
{
    private static readonly (PathString Prefix, string Code, bool Addon)[] Rules =
    [
        ("/api/integrations/rappi", "rappi", true),
        ("/api/whatsapp", "whatsapp_ai", true),
        ("/api/delivery-tracking", "delivery_tracking", false),
        ("/api/delivery-incidents", "delivery_tracking", false),
        ("/api/delivery-alerts", "delivery_tracking", false),
        ("/api/delivery-routing", "delivery_routing", false),
        ("/api/expenses/menu", "cost_attribution", false),
        ("/api/expenses", "expenses", false),
        ("/api/suppliers", "expenses", false),
        ("/api/cash-register", "cash_register", false),
        ("/api/business-documents", "business_documents", false),
        ("/api/documents", "business_documents", false),
        ("/api/customers", "customers", false),
        ("/api/addresses", "customers", false),
        ("/api/products", "catalog", false),
        ("/api/productcategories", "catalog", false),
        ("/api/users", "users", false),
        ("/api/orders", "pos", false)
    ];

    public async Task InvokeAsync(HttpContext context, ITenantCapabilityService capabilities)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var rule = Rules.FirstOrDefault(candidate => context.Request.Path.StartsWithSegments(candidate.Prefix));
            if (!string.IsNullOrEmpty(rule.Code))
            {
                var allowed = rule.Addon
                    ? await capabilities.HasAddonAsync(rule.Code, context.RequestAborted)
                    : await capabilities.HasModuleAsync(rule.Code, context.RequestAborted);
                if (!allowed)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { code = "TENANT_CAPABILITY_REQUIRED", message = "El módulo no está habilitado para el tenant." }, context.RequestAborted);
                    return;
                }
            }
        }
        await next(context);
    }
}
