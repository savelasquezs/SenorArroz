using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using SenorArroz.API.Controllers;
using SenorArroz.API.Security;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class DataGatewayOAuthTests
{
    [Theory]
    [InlineData("https://chatgpt.com/connector_platform_oauth_redirect", true)]
    [InlineData("https://chatgpt.com/connector/oauth/abcdef012345", true)]
    [InlineData("http://127.0.0.1:45678/callback", true)]
    [InlineData("http://localhost:45678/callback/server-id", true)]
    [InlineData("https://chatgpt.com.evil.example/connector_platform_oauth_redirect", false)]
    [InlineData("https://chatgpt.com/redirect?to=evil", false)]
    [InlineData("https://user:secret@chatgpt.com/connector_platform_oauth_redirect", false)]
    [InlineData("http://192.168.1.2:4000/callback", false)]
    [InlineData("https://evil.example/callback", false)]
    public void Registration_restricts_redirect_origins_and_paths(string uri, bool expected) => Assert.Equal(expected, DataGatewayOAuth.AllowedRedirect(uri));

    [Fact]
    public void Tenant_superadmins_require_explicit_enablement()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var user = new User { Id = 7, Active = true, TenantId = 1, Role = UserRole.Superadmin,
            Tenant = new Tenant { Id = 1, IsActive = true, Status = TenantStatus.Active },
            Branch = new Branch { Id = 1, TenantId = 1 } };
        Assert.False(DataGatewayOAuth.CanAuthorize(user, config));
        config["DataGateway:OAuth:AllowTenantSuperadmins"] = "true";
        Assert.True(DataGatewayOAuth.CanAuthorize(user, config));
        user.AccountType = EmployeeAccountType.Terminal;
        Assert.False(DataGatewayOAuth.CanAuthorize(user, config));
    }

    [GatewayPostgreSqlFact]
    public async Task Hosted_oauth_enforces_consent_pkce_one_time_codes_refresh_revocation_and_tenant_access()
    {
        await VerifyHostedOAuth(true);
        await VerifyHostedOAuth(false);
    }

    private static async Task VerifyHostedOAuth(bool includeResource)
    {
        var source = Environment.GetEnvironmentVariable("FLOW_POSTGRES_TEST_CONNECTION")!;
        Assert.Contains(new NpgsqlConnectionStringBuilder(source).Host, new[] { "localhost", "127.0.0.1", "::1", "flow-postgres" });
        var database = "gateway_oauth_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source);
        await admin.OpenAsync();
        await Execute(admin, $"CREATE DATABASE {database}");
        var connection = new NpgsqlConnectionStringBuilder(source) { Database = database, Pooling = false }.ConnectionString;
        var createdRole = false;
        try
        {
            await Execute(admin, "CREATE ROLE senorarroz_runtime LOGIN PASSWORD 'oauth-runtime-test-only' NOSUPERUSER NOBYPASSRLS");
            createdRole = true;
            await using (var setup = new NpgsqlConnection(connection))
            {
                await setup.OpenAsync();
                await using var transaction = await setup.BeginTransactionAsync();
                var migration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts/add_data_gateway_oauth_20261110.sql"));
                await Execute(setup, migration, transaction);
                await Execute(setup, migration, transaction);
                await transaction.CommitAsync();
                Assert.Equal(4L, await new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='gateway_oauth' AND c.relkind='r' AND c.relrowsecurity AND c.relforcerowsecurity", setup).ExecuteScalarAsync());
            }
            var runtime = new NpgsqlConnectionStringBuilder(connection) { Username = "senorarroz_runtime", Password = "oauth-runtime-test-only" }.ConnectionString;
            await using (var restricted = new NpgsqlConnection(runtime))
            {
                await restricted.OpenAsync();
                var denied = await Assert.ThrowsAsync<PostgresException>(() => Execute(restricted,
                    "INSERT INTO gateway_oauth.application (id,tenant_id) VALUES ('other-tenant',2)"));
                Assert.Equal("42501", denied.SqlState);
            }
            using var rsa = RSA.Create(3072);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataGateway:Enabled"] = "true", ["DataGateway:ReaderTenantId"] = "1",
                ["DataGateway:OAuth:HostedEnabled"] = "true",
                ["DataGateway:OAuth:Authority"] = "https://gateway.example/api/internal/data-gateway/oauth/",
                ["DataGateway:OAuth:Resource"] = "https://gateway.example/api/internal/data-gateway/mcp",
                ["DataGateway:OAuth:SigningKey"] = rsa.ExportPkcs8PrivateKeyPem(),
                ["DataGateway:OAuth:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["DataGateway:OAuth:AllowedSubjects:0"] = "7", ["DataGateway:AllowedViews:0"] = "diagnostics.orders_summary",
                ["ConnectionStrings:DefaultConnection"] = runtime,
                ["ConnectionStrings:DataGatewayReader"] = "Host=unused;Username=senorarroz_db_reader"
            }).Build();
            var session = Guid.NewGuid();
            var user = new User { Id = 7, TenantId = 1, Active = true, Role = UserRole.Superadmin, ActiveSessionId = session,
                Tenant = new Tenant { Id = 1, IsActive = true, Status = TenantStatus.Active, AccessVersion = 1 },
                Branch = new Branch { Id = 1, TenantId = 1 } };
            var repo = new Mock<IAuthRepository>();
            repo.Setup(r => r.GetUserByEmailAsync("owner@example.test", It.IsAny<CancellationToken>())).ReturnsAsync(user);
            repo.Setup(r => r.GetUserByIdWithBranchAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            repo.Setup(r => r.ValidatePasswordAsync(user, "test-login-only", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            using var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(config); services.AddSingleton(repo.Object);
                services.AddLogging(); services.AddHttpContextAccessor(); services.AddScoped<ICurrentTenant, CurrentTenantService>();
                services.AddControllers().AddApplicationPart(typeof(DataGatewayController).Assembly);
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new()
                { ValidIssuer = "local", ValidAudience = "local", IssuerSigningKey = new SymmetricSecurityKey(new byte[64]) });
                services.AddDataGateway(config);
            }).Configure(app =>
            {
                app.UseRouting(); app.UseHostedDataGatewayOAuth(config); app.UseAuthentication();
                app.UseAuthorization(); app.UseRateLimiter(); app.UseEndpoints(e => e.MapControllers());
            }));
            using var client = server.CreateClient();
            client.BaseAddress = new Uri("https://gateway.example");
            var prefix = DataGatewayOAuth.Prefix;
            var discovery = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-authorization-server"));
            Assert.Contains("S256", discovery.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(v => v.GetString()));
            Assert.DoesNotContain("plain", discovery.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(new[] { "none" }, discovery.RootElement.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(v => v.GetString()));
            Assert.DoesNotContain("openid", discovery.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(prefix + "/register", new { redirect_uris = new[] { "https://evil.example/callback" } })).StatusCode);
            var registration = await client.PostAsJsonAsync(prefix + "/register", new { redirect_uris = new[] { "https://chatgpt.com/connector_platform_oauth_redirect" }, token_endpoint_auth_method = "none" });
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
            var clientId = JsonDocument.Parse(await registration.Content.ReadAsStringAsync()).RootElement.GetProperty("client_id").GetString()!;
            var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            var parameters = new Dictionary<string, string?>
            {
                ["client_id"] = clientId, ["redirect_uri"] = "https://chatgpt.com/connector_platform_oauth_redirect",
                ["response_type"] = "code", ["scope"] = "gateway:read offline_access", ["state"] = "state-test",
                ["resource"] = config["DataGateway:OAuth:Resource"], ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256"
            };
            var authorizeUri = QueryHelpers.AddQueryString(prefix + "/authorize", parameters);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(QueryHelpers.AddQueryString(prefix + "/authorize",
                new Dictionary<string, string?>(parameters) { ["resource"] = "https://other.example/mcp" }))).StatusCode);
            if (!includeResource)
            {
                parameters.Remove("resource");
                authorizeUri = QueryHelpers.AddQueryString(prefix + "/authorize", parameters);
            }
            var consent = await client.GetAsync(authorizeUri);
            Assert.True(consent.StatusCode == HttpStatusCode.OK, await consent.Content.ReadAsStringAsync());
            var html = await consent.Content.ReadAsStringAsync();
            Assert.DoesNotContain("value=\"test-login-only\"", html);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            client.DefaultRequestHeaders.Add("Cookie", consent.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
            var form = parameters.ToDictionary(k => k.Key, k => k.Value ?? "");
            form["email"] = "owner@example.test"; form["password"] = "test-login-only"; form["consent"] = "read";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(prefix + "/authorize", new FormUrlEncodedContent(form))).StatusCode);
            form["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
            var granted = await client.PostAsync(prefix + "/authorize", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            var redirect = QueryHelpers.ParseQuery(granted.Headers.Location!.Query);
            Assert.Equal("state-test", redirect["state"].ToString()); Assert.Equal(DataGatewayOAuth.Issuer(config), redirect["iss"].ToString());
            var code = redirect["code"].ToString();
            var exchange = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = clientId,
                ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = parameters["redirect_uri"]! };
            if (includeResource) exchange["resource"] = config["DataGateway:OAuth:Resource"]!;
            var wrongVerifier = new Dictionary<string, string>(exchange) { ["code_verifier"] = new string('x', 43) };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(prefix + "/token", new FormUrlEncodedContent(wrongVerifier))).StatusCode);
            var exchanged = await client.PostAsync(prefix + "/token", new FormUrlEncodedContent(exchange));
            Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
            var tokens = JsonDocument.Parse(await exchanged.Content.ReadAsStringAsync()).RootElement;
            var access = tokens.GetProperty("access_token").GetString()!; var refresh = tokens.GetProperty("refresh_token").GetString()!;
            Assert.Contains(DataGatewayOAuth.Resource(config), new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(access).Audiences);
            async Task<HttpStatusCode> Ping(string bearer)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/data-gateway/mcp");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "ping" });
                return (await client.SendAsync(request)).StatusCode;
            }
            Assert.Equal(HttpStatusCode.OK, await Ping(access)); Assert.Equal(session, user.ActiveSessionId);
            user.Active = false; Assert.Equal(HttpStatusCode.Forbidden, await Ping(access)); user.Active = true;
            user.Role = UserRole.Cashier; Assert.Equal(HttpStatusCode.Forbidden, await Ping(access)); user.Role = UserRole.Superadmin;
            user.TenantId = 2; Assert.Equal(HttpStatusCode.Forbidden, await Ping(access)); user.TenantId = 1;
            user.Tenant.AccessVersion = 2; Assert.Equal(HttpStatusCode.Forbidden, await Ping(access)); user.Tenant.AccessVersion = 1;
            var refreshForm = new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = refresh };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(prefix + "/token", new FormUrlEncodedContent(
                new Dictionary<string, string>(refreshForm) { ["resource"] = "https://other.example/mcp" }))).StatusCode);
            if (includeResource) refreshForm["resource"] = config["DataGateway:OAuth:Resource"]!;
            var renewed = await client.PostAsync(prefix + "/token", new FormUrlEncodedContent(refreshForm));
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            var renewedTokens = JsonDocument.Parse(await renewed.Content.ReadAsStringAsync()).RootElement;
            var newAccess = renewedTokens.GetProperty("access_token").GetString()!;
            Assert.Equal(HttpStatusCode.OK, await Ping(newAccess));
            var revoked = await client.PostAsync(prefix + "/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["client_id"] = clientId, ["token"] = newAccess, ["token_type_hint"] = "access_token" }));
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, await Ping(newAccess));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(prefix + "/token", new FormUrlEncodedContent(exchange))).StatusCode);
            repo.Verify(r => r.EndSessionIfCurrentAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools(); await Execute(admin, $"DROP DATABASE {database} WITH (FORCE)");
            if (createdRole) await Execute(admin, "DROP ROLE senorarroz_runtime");
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    { await using var command = new NpgsqlCommand(sql, connection, transaction); await command.ExecuteNonQueryAsync(); }
}
