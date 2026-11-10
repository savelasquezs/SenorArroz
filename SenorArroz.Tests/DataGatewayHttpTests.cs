using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SenorArroz.API.Controllers;
using SenorArroz.API.Security;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class DataGatewayHttpTests
{
    [Fact]
    public async Task Http_authentication_validates_signatures_expiry_audience_scope_subject_and_tenant()
    {
        const string authority = "https://issuer.example";
        const string resource = "https://gateway.example/api/internal/data-gateway/mcp";
        using var rsa = RSA.Create(2048);
        var oauthKey = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var localKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 64)));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataGateway:Enabled"] = "true", ["DataGateway:AllowedViews:0"] = "diagnostics.orders_summary",
            ["DataGateway:OAuth:Authority"] = authority, ["DataGateway:OAuth:Resource"] = resource,
            ["DataGateway:OAuth:AllowedSubjects:0"] = "owner",
            ["ConnectionStrings:DataGatewayReader"] = "Host=unused;Username=senorarroz_db_reader"
        }).Build();
        using var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentTenant, CurrentTenantService>();
            services.AddControllers().AddApplicationPart(typeof(DataGatewayController).Assembly);
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                options.TokenValidationParameters = new()
                {
                    ValidIssuer = "local", ValidAudience = "local-api", IssuerSigningKey = localKey,
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero
                });
            services.AddDataGateway(config);
            services.PostConfigure<JwtBearerOptions>(DataGatewaySecurity.Scheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = authority };
                metadata.SigningKeys.Add(oauthKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        }).Configure(app =>
        {
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
        using var client = server.CreateClient();
        async Task<HttpResponseMessage> Call(string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/data-gateway/mcp");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            request.Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""", Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }
        string Token(bool oauth = true, string tenant = "1", string scope = "gateway:read", string sub = "owner",
            string? audience = null, bool expired = false, bool wrongKey = false, string role = "Superadmin")
        {
            var key = wrongKey ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('y', 64))) : oauth ? (SecurityKey)oauthKey : localKey;
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(oauth ? authority : "local",
                audience ?? (oauth ? resource : "local-api"),
                [new("sub", sub), new("tenant_id", tenant), new("scope", scope), new(ClaimTypes.Role, role)],
                notBefore: DateTime.UtcNow.AddMinutes(-10), expires: DateTime.UtcNow.AddMinutes(expired ? -2 : 5),
                signingCredentials: new SigningCredentials(key, key is RsaSecurityKey ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.HmacSha256)));
        }
        var unauthenticated = await Call(null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Contains("resource_metadata=", unauthenticated.Headers.WwwAuthenticate.ToString());
        foreach (var token in new[] { Token(expired: true), Token(audience: "wrong"), Token(wrongKey: true), "malformed" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Call(token)).StatusCode);
        foreach (var token in new[] { Token(tenant: "2"), Token(scope: "other"), Token(sub: "other"), Token(oauth: false, role: "Cashier") })
            Assert.Equal(HttpStatusCode.Forbidden, (await Call(token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Call(Token())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Call(Token(oauth: false))).StatusCode);
        var discovery = await client.GetStringAsync("/.well-known/oauth-protected-resource/api/internal/data-gateway/mcp");
        Assert.Contains(resource, discovery);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/api/internal/data-gateway/mcp")).StatusCode);
    }
}
