using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SenorArroz.Tests.Support;

namespace SenorArroz.Tests;

/// <summary>
/// Comprueba que el rate limiting configurado en Program.cs limita por IP y exime Swagger.
/// Cada test usa su propio host para no compartir contadores del limitador.
/// </summary>
public class RateLimitingIntegrationTests
{
    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Swagger_openapi_json_is_not_rate_limited_under_global_cap()
    {
        using var factory = new RateLimitApiWebApplicationFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 8; i++)
        {
            var res = await client.GetAsync("/swagger/v1/swagger.json");
            Assert.True(
                res.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
                $"Iteración {i}: esperaba 200 o 404, obtuve {res.StatusCode}");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
        }
    }

    [Fact]
    public async Task Unknown_api_route_hits_global_limit_and_returns_429()
    {
        using var factory = new RateLimitApiWebApplicationFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
            last = await client.GetAsync("/api/__rate_limit_probe__" + i);

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);

        var body = await last.Content.ReadFromJsonAsync<RateLimitErrorBody>(JsonRead);
        Assert.NotNull(body);
        Assert.Equal("TooManyRequests", body.Error);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    [Fact]
    public async Task Auth_login_hits_stricter_policy_and_returns_429()
    {
        using var factory = new RateLimitApiWebApplicationFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync(
                "/api/Auth/login",
                new { email = "nobody@test.local", password = "wrongpwd" });
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
    }

    private sealed record RateLimitErrorBody(string Error, string Message);
}
