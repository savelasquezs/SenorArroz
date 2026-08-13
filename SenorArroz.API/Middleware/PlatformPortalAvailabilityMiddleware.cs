using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Middleware;

public sealed class PlatformPortalAvailabilityMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        var platformPath = context.Request.Path.StartsWithSegments("/api/platform")
            || context.Request.Path.StartsWithSegments("/api/tenant-invitations");
        if (!platformPath)
        {
            await next(context);
            return;
        }

        var enabled = configuration.GetValue("Saas:PortalEnabled", true);
        var stored = await db.PlatformSettings.AsNoTracking()
            .Where(x => x.Key == "portal_enabled")
            .Select(x => x.ValueJson)
            .SingleOrDefaultAsync(context.RequestAborted);
        if (stored is not null) enabled = ParseBoolean(stored, enabled);

        if (!enabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { code = "PLATFORM_PORTAL_DISABLED", message = "El portal SaaS estÃ¡ temporalmente deshabilitado." }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool ParseBoolean(string json, bool fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(document.RootElement.GetString(), out var value) => value,
                _ => fallback
            };
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
