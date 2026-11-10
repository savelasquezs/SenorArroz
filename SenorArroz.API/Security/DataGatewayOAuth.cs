using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Helpers;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Application.Common.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SenorArroz.API.Security;

public static class DataGatewayOAuth
{
    public const string Prefix = "/api/internal/data-gateway/oauth";
    public static bool Hosted(IConfiguration config) => config.GetValue<bool>("DataGateway:OAuth:HostedEnabled");
    public static string Issuer(IConfiguration config) => config["DataGateway:OAuth:Authority"]!.TrimEnd('/') + "/";
    public static string Resource(IConfiguration config) => config["DataGateway:OAuth:Resource"]!;
    public static bool MatchesResource(OpenIddictRequest request, IConfiguration config) =>
        request.GetResources().IsDefaultOrEmpty || request.GetResources().SequenceEqual([Resource(config)]);

    public static RSA SigningKey(IConfiguration config)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(config["DataGateway:OAuth:SigningKey"]);
        if (rsa.KeySize < 3072) throw new InvalidOperationException("Gateway OAuth requires RSA-3072 or stronger.");
        return rsa;
    }

    public static bool CanAuthorize(User? user, IConfiguration config) => user is { Active: true }
        && TenantAccessRules.CanAuthenticate(user) && user.Role == UserRole.Superadmin && !user.IsTerminal
        && user.TenantId == config.GetValue("DataGateway:ReaderTenantId", 1)
        && ((config.GetSection("DataGateway:OAuth:AllowedSubjects").Get<string[]>() ?? [])
            .Contains(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
            || config.GetValue<bool>("DataGateway:OAuth:AllowTenantSuperadmins"));

    public static bool AllowedRedirect(string value)
    {
        if (value.Length > 2048) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query)) return false;
        if (uri.Scheme == "https" && uri.Host == "chatgpt.com" && uri.IsDefaultPort)
            return uri.AbsolutePath == "/connector_platform_oauth_redirect"
                || (uri.AbsolutePath.StartsWith("/connector/oauth/", StringComparison.Ordinal)
                    && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"\A/connector/oauth/[a-zA-Z0-9_-]{8,128}\z"));
        return uri.Scheme == "http" && uri.Host is "127.0.0.1" or "localhost" or "[::1]"
            && uri.Port is >= 1024 and <= 65535
            && (uri.AbsolutePath == "/callback" || uri.AbsolutePath.StartsWith("/callback/", StringComparison.Ordinal));
    }

    public static IServiceCollection AddHostedDataGatewayOAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-GatewayConsent";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        if (!Hosted(config)) return services;
        var signing = new RsaSecurityKey(SigningKey(config));
        var encryption = Convert.FromBase64String(config["DataGateway:OAuth:EncryptionKey"]!);
        if (encryption.Length != 32) throw new InvalidOperationException("Gateway OAuth requires a 256-bit encryption key.");
        services.AddDbContext<DataGatewayOAuthDbContext>(options =>
            options.UseNpgsql(config.GetConnectionString("DefaultConnection"), p => p.CommandTimeout(5)).UseOpenIddict());
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<DataGatewayOAuthDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(Issuer(config)));
                options.SetAuthorizationEndpointUris(Prefix + "/authorize");
                options.SetTokenEndpointUris(Prefix + "/token");
                options.SetRevocationEndpointUris(Prefix + "/revoke");
                options.SetConfigurationEndpointUris("/.well-known/oauth-authorization-server",
                    "/.well-known/oauth-authorization-server" + Prefix,
                    "/.well-known/oauth-authorization-server" + Prefix + "/");
                options.SetJsonWebKeySetEndpointUris(Prefix + "/jwks");
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                options.Configure(settings => settings.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
                options.RegisterScopes(DataGatewaySecurity.Scope, Scopes.OfflineAccess);
                options.RegisterResources(Resource(config));
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));
                options.AddSigningKey(signing).AddEncryptionKey(new SymmetricSecurityKey(encryption));
                options.DisableAccessTokenEncryption();
                options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
                options.AddEventHandler<OpenIddictServerEvents.ApplyConfigurationResponseContext>(builder => builder.UseInlineHandler(context =>
                {
                    context.Response["registration_endpoint"] = new Uri(new Uri(Issuer(config)), Prefix + "/register").AbsoluteUri;
                    context.Response["authorization_response_iss_parameter_supported"] = true;
                    context.Response["token_endpoint_auth_methods_supported"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { "none" });
                    context.Response["revocation_endpoint_auth_methods_supported"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { "none" });
                    context.Response["scopes_supported"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { DataGatewaySecurity.Scope, Scopes.OfflineAccess });
                    return default;
                }));
                options.AddEventHandler<OpenIddictServerEvents.ApplyAuthorizationResponseContext>(builder => builder.UseInlineHandler(context =>
                {
                    context.Response["iss"] = Issuer(config);
                    return default;
                }));
            })
            .AddValidation(options =>
            {
                options.AddAudiences(Resource(config));
                options.UseLocalServer();
                options.EnableTokenEntryValidation();
                options.EnableAuthorizationEntryValidation();
                options.Configure(settings => settings.TokenValidationParameters.AuthenticationType = DataGatewaySecurity.Scheme);
                options.UseAspNetCore();
            });
        services.AddLogging(options => options.AddFilter("OpenIddict", LogLevel.Warning));
        return services;
    }

    public static IApplicationBuilder UseHostedDataGatewayOAuth(this IApplicationBuilder app, IConfiguration config)
    {
        if (!Hosted(config)) return app;
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter((context.Request.Path.StartsWithSegments(Prefix) ? "oauth:" : "read:")
                + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = context.Request.Path.StartsWithSegments(Prefix) ? 30 : 120,
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/internal/data-gateway")) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            using var lease = limiter.AttemptAcquire(context);
            if (!lease.IsAcquired) { context.Response.StatusCode = 429; return; }
            if (context.Request.ContentLength > 32768) { context.Response.StatusCode = 413; return; }
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = 32768;
            await next(context);
        });
    }

    public static ClaimsPrincipal Principal(User user, string resource, bool offline)
    {
        var identity = new ClaimsIdentity("GatewayConsent", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        identity.SetClaim("tenant_id", user.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        identity.SetClaim("tenant_access_version", user.Tenant.AccessVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        identity.SetClaim(Claims.Role, "Superadmin");
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(offline ? [DataGatewaySecurity.Scope, Scopes.OfflineAccess] : [DataGatewaySecurity.Scope]);
        principal.SetResources(resource);
        principal.SetDestinations(_ => [Destinations.AccessToken]);
        return principal;
    }
}
