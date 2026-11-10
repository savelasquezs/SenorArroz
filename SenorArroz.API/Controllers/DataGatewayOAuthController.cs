using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SenorArroz.API.Security;
using SenorArroz.Domain.Interfaces.Repositories;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SenorArroz.API.Controllers;

[ApiController]
[AllowAnonymous]
[RequestSizeLimit(32768)]
[Route(DataGatewayOAuth.Prefix)]
public sealed class DataGatewayOAuthController(IConfiguration config, IAntiforgery antiforgery, IServiceProvider services) : ControllerBase
{
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), 11);
    private static readonly SemaphoreSlim RegistrationConcurrency = new(2, 2);
    private bool Ready => DataGatewayOAuth.Hosted(config);
    private static string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? "");

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!Ready) return NotFound();
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("redirect_uris", out var redirects)
            || redirects.ValueKind != JsonValueKind.Array || redirects.GetArrayLength() is < 1 or > 3
            || redirects.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || !DataGatewayOAuth.AllowedRedirect(v.GetString()!))
            || body.TryGetProperty("token_endpoint_auth_method", out var method) && (method.ValueKind != JsonValueKind.String || method.GetString() != "none")
            || body.TryGetProperty("grant_types", out var grants) && (grants.ValueKind != JsonValueKind.Array
                || grants.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || v.GetString() is not (GrantTypes.AuthorizationCode or GrantTypes.RefreshToken)))
            || body.TryGetProperty("scope", out var scope) && (scope.ValueKind != JsonValueKind.String
                || scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(s => s is not (DataGatewaySecurity.Scope or Scopes.OfflineAccess))))
            return BadRequest(new { error = "invalid_client_metadata" });
        var uris = redirects.EnumerateArray().Select(v => v.GetString()!).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        var clientId = "gateway-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', uris)))).ToLowerInvariant();
        if (!await RegistrationConcurrency.WaitAsync(0, ct)) return StatusCode(429);
        try
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(clientId, ct) is null)
            {
                if (await applications.CountAsync(ct) >= 128) return StatusCode(429);
                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId, ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Explicit,
                    DisplayName = "Señor Arroz · ChatGPT / Codex"
                };
                foreach (var uri in uris) descriptor.RedirectUris.Add(new Uri(uri));
                descriptor.Permissions.UnionWith([
                    Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + DataGatewaySecurity.Scope,
                    Permissions.Prefixes.Resource + DataGatewayOAuth.Resource(config)
                ]);
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
                try { await applications.CreateAsync(descriptor, ct); }
                catch (OpenIddictExceptions.ValidationException)
                {
                    if (await applications.FindByClientIdAsync(clientId, ct) is null) throw;
                }
            }
            return StatusCode(201, new
            {
                client_id = clientId, client_name = "Señor Arroz", redirect_uris = uris,
                token_endpoint_auth_method = "none", grant_types = new[] { GrantTypes.AuthorizationCode, GrantTypes.RefreshToken },
                response_types = new[] { ResponseTypes.Code }, scope = DataGatewaySecurity.Scope + " " + Scopes.OfflineAccess
            });
        }
        finally { RegistrationConcurrency.Release(); }
    }

    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        if (!Ready) return NotFound();
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request is null || !DataGatewayOAuth.MatchesResource(request, config)
            || request.CodeChallengeMethod != CodeChallengeMethods.Sha256
            || !request.GetScopes().Contains(DataGatewaySecurity.Scope)
            || request.GetScopes().Any(s => s is not (DataGatewaySecurity.Scope or Scopes.OfflineAccess)))
            return BadRequest(new { error = "invalid_request" });
        if (HttpMethods.IsGet(Request.Method)) return Consent();
        try { await antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException) { return BadRequest(new { error = "invalid_request" }); }
        var form = await Request.ReadFormAsync(ct);
        var email = form["email"].ToString();
        var password = form["password"].ToString();
        if (email.Length is < 1 or > 254 || password.Length is < 1 or > 256 || form["consent"] != "read")
            return Consent("No fue posible autorizar el acceso.");
        var repo = services.GetRequiredService<IAuthRepository>();
        var user = await repo.GetUserByEmailAsync(email.Trim(), ct);
        var validPassword = user is not null
            ? await repo.ValidatePasswordAsync(user, password, ct) : BCrypt.Net.BCrypt.Verify(password, DummyPasswordHash);
        if (!validPassword || !DataGatewayOAuth.CanAuthorize(user, config))
            return Consent("No fue posible autorizar el acceso.");
        var principal = DataGatewayOAuth.Principal(user!, DataGatewayOAuth.Resource(config), request.HasScope(Scopes.OfflineAccess));
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("token")]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        if (!Ready) return NotFound();
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request is null || !DataGatewayOAuth.MatchesResource(request, config)
            || !(request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType()))
            return BadRequest(new { error = "invalid_request" });
        var auth = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal is null
            || !int.TryParse(auth.Principal.GetClaim(Claims.Subject), out var id)
            || !long.TryParse(auth.Principal.GetClaim("tenant_access_version"), out var version)) return InvalidGrant();
        var user = await services.GetRequiredService<IAuthRepository>().GetUserByIdWithBranchAsync(id, ct);
        if (!DataGatewayOAuth.CanAuthorize(user, config) || user!.Tenant.AccessVersion != version) return InvalidGrant();
        return SignIn(auth.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private IActionResult InvalidGrant() => Forbid(new AuthenticationProperties(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Authorization is no longer valid."
    }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private IActionResult Consent(string? error = null)
    {
        var request = HttpContext.GetOpenIddictServerRequest()!;
        var callbackOrigin = new Uri(request.RedirectUri!).GetLeftPart(UriPartial.Authority);
        Response.Headers["Content-Security-Policy"] = $"default-src 'none'; style-src 'unsafe-inline'; form-action 'self' {callbackOrigin}; frame-ancestors 'none'; base-uri 'none'";
        Response.Headers["X-Frame-Options"] = "DENY";
        var token = antiforgery.GetAndStoreTokens(HttpContext);
        var hidden = new StringBuilder();
        foreach (var name in new[] { "client_id", "redirect_uri", "response_type", "scope", "state", "code_challenge", "code_challenge_method", "resource" })
            hidden.Append($"<input type=\"hidden\" name=\"{name}\" value=\"{Encode((string?)request[name])}\">");
        hidden.Append($"<input type=\"hidden\" name=\"{Encode(token.FormFieldName)}\" value=\"{Encode(token.RequestToken)}\">");
        return Content($$"""
            <!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Autorizar lectura · Señor Arroz</title><style>body{font:16px system-ui;background:#f5f3ef;color:#28231d;margin:0}main{max-width:430px;margin:8vh auto;padding:28px;background:white;border-radius:16px}h1{font-size:25px}label{display:block;margin:18px 0}input[type=email],input[type=password]{box-sizing:border-box;width:100%;padding:12px;margin-top:7px;border:1px solid #aaa;border-radius:7px}button{width:100%;padding:14px;background:#754219;color:white;border:0;border-radius:7px;font-weight:700}.error{color:#a21c1c}</style>
            <main><h1>Conectar Señor Arroz</h1><p>Autoriza a ChatGPT o Codex a consultar únicamente las vistas de diagnóstico permitidas de Señor Arroz.</p>
            <p>No podrá modificar pedidos, pagos ni inventario. Esta autorización no cierra la sesión del POS.</p><p class="error">{{Encode(error)}}</p>
            <form method="post" action="{{DataGatewayOAuth.Prefix}}/authorize">{{hidden}}
            <label>Correo de Señor Arroz<input name="email" type="email" autocomplete="username" maxlength="254" required></label>
            <label>Contraseña<input name="password" type="password" autocomplete="current-password" maxlength="256" required></label>
            <label><input name="consent" type="checkbox" value="read" required> Autorizo la consulta de solo lectura.</label>
            <button type="submit">Autorizar solo lectura</button></form></main></html>
            """, "text/html; charset=utf-8");
    }
}
