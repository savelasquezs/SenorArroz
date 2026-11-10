using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

namespace SenorArroz.API.Security;

public static class DataGatewaySecurity
{
    public const string Policy = "DataGatewayRead";
    public const string Scheme = "DataGatewayOAuth";
    public const string Scope = "gateway:read";

    public static bool Authorized(ClaimsPrincipal user, IConfiguration config) =>
        user.Identities.Any(identity => identity.IsAuthenticated &&
            int.TryParse(identity.FindFirst("tenant_id")?.Value, out var tenantId) &&
            tenantId == config.GetValue("DataGateway:ReaderTenantId", 1) &&
            (identity.AuthenticationType == Scheme
                ? identity.FindAll("scope").SelectMany(c => c.Value.Split(' ')).Contains(Scope) &&
                  (config.GetSection("DataGateway:OAuth:AllowedSubjects").Get<string[]>() ?? [])
                    .Contains(identity.FindFirst("sub")?.Value, StringComparer.Ordinal)
                : identity.HasClaim(identity.RoleClaimType, "Superadmin")));

    public static IServiceCollection AddDataGateway(this IServiceCollection services, IConfiguration config)
    {
        var authority = config["DataGateway:OAuth:Authority"];
        var resource = config["DataGateway:OAuth:Resource"];
        var oauth = Uri.TryCreate(authority, UriKind.Absolute, out var issuer) && issuer.Scheme == "https"
            && Uri.TryCreate(resource, UriKind.Absolute, out var audience) && audience.Scheme == "https";
        if (!string.IsNullOrEmpty(authority) && !oauth)
            throw new InvalidOperationException("Data Gateway OAuth requires HTTPS authority and resource.");
        if (oauth)
            services.AddAuthentication().AddJwtBearer(Scheme, options =>
            {
                options.Authority = authority;
                options.Audience = resource;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    AuthenticationType = Scheme, ValidateIssuer = true, ValidateAudience = true,
                    ValidateLifetime = true, ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(15), NameClaimType = "sub"
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        context.Response.Headers.WWWAuthenticate =
                            $"Bearer resource_metadata=\"{MetadataUrl(resource!)}\", scope=\"{Scope}\"";
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthentication().AddPolicyScheme(Policy, Policy, options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var token = context.Request.Headers.Authorization.ToString();
                if (oauth && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token[7..]);
                        if (jwt.Issuer.TrimEnd('/') == authority!.TrimEnd('/')) return Scheme;
                    }
                    catch (ArgumentException) { }
                }
                return JwtBearerDefaults.AuthenticationScheme;
            };
            options.ForwardChallenge = oauth ? Scheme : JwtBearerDefaults.AuthenticationScheme;
        });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(Policy);
            policy.RequireAuthenticatedUser().RequireAssertion(c => Authorized(c.User, config));
        }));
        services.AddRateLimiter(options => options.AddPolicy(Policy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("tenant_id")?.Value ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    AutoReplenishment = true
                })));
        return services;
    }

    public static string MetadataUrl(string resource) =>
        new Uri(new Uri(resource), "/.well-known/oauth-protected-resource" + new Uri(resource).AbsolutePath).AbsoluteUri;
}
