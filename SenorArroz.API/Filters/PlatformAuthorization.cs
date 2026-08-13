using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PlatformAuthorizeAttribute : TypeFilterAttribute
{
    public PlatformAuthorizeAttribute() : base(typeof(PlatformSessionFilter)) { }
}

public sealed class PlatformSessionFilter : IAsyncAuthorizationFilter
{
    public const string SessionCookie = "sa_platform_session";
    public const string TrustedDeviceCookie = "sa_platform_device";
    public const string CsrfCookie = "sa_platform_csrf";
    public const string CsrfHeader = "X-Platform-CSRF";

    private readonly IPlatformAuthService _auth;
    private readonly IPlatformCurrentUser _currentUser;

    public PlatformSessionFilter(IPlatformAuthService auth, IPlatformCurrentUser currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        var requireCsrf = method is not "GET" and not "HEAD" and not "OPTIONS";
        var session = await _auth.ValidateSessionAsync(
            context.HttpContext.Request.Cookies[SessionCookie],
            context.HttpContext.Request.Headers[CsrfHeader].FirstOrDefault(),
            requireCsrf,
            context.HttpContext.RequestAborted);
        if (session is null)
        {
            context.Result = new UnauthorizedObjectResult(new { message = requireCsrf ? "Sesión o CSRF inválido." : "Sesión de plataforma inválida." });
            return;
        }
        _currentUser.Id = session.Id;
        _currentUser.Name = session.Name;
        _currentUser.Email = session.Email;
        _currentUser.IsAuthenticated = true;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TenantCapabilityAttribute : TypeFilterAttribute
{
    public TenantCapabilityAttribute(string code, bool addon = false) : base(typeof(TenantCapabilityFilter))
    {
        Arguments = [code, addon];
    }
}

public sealed class TenantCapabilityFilter : IAsyncAuthorizationFilter
{
    private readonly string _code;
    private readonly bool _addon;
    private readonly ITenantCapabilityService _capabilities;

    public TenantCapabilityFilter(string code, bool addon, ITenantCapabilityService capabilities)
    {
        _code = code;
        _addon = addon;
        _capabilities = capabilities;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) return;
        var enabled = _addon
            ? await _capabilities.HasAddonAsync(_code, context.HttpContext.RequestAborted)
            : await _capabilities.HasModuleAsync(_code, context.HttpContext.RequestAborted);
        if (!enabled) context.Result = new ObjectResult(new { message = "El módulo no está habilitado para el tenant." }) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
