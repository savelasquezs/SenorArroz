using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.API.Hosting;

namespace SenorArroz.Tests;

public sealed class PublicCatalogCompressionTests
{
    private const string Catalog = "/api/public/storefront/catalog";
    private static readonly string Payload = JsonSerializer.Serialize(new
    {
        riceGroups = Enumerable.Range(1, 60).Select(id => new
        {
            id, name = "Synthetic product " + id,
            description = "Synthetic catalog description with repeated fields and ingredients.",
            price = 10000 + id
        }),
        branches = Array.Empty<object>()
    });

    private static TestServer Server() => new(new WebHostBuilder()
        .ConfigureServices(services => services.AddPublicCatalogCompression())
        .Configure(app =>
        {
            // Stand-in for the earlier authorization middleware in ApiHost.
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("X-Deny-Fixture"))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized fixture");
                    return;
                }
                await next(context);
            });
            app.UsePublicCatalogCompression();
            app.Run(async context =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(Payload);
            });
        }));

    [Fact]
    public async Task Catalog_over_https_negotiates_gzip_and_preserves_exact_json()
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        using var plain = await client.GetAsync(Catalog);
        var plainBytes = await plain.Content.ReadAsByteArrayAsync();
        Assert.Empty(plain.Content.Headers.ContentEncoding);
        using var request = new HttpRequestMessage(HttpMethod.Get, Catalog);
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        using var compressed = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, compressed.StatusCode);
        Assert.Contains("gzip", compressed.Content.Headers.ContentEncoding);
        Assert.Contains(compressed.Headers.Vary, value => string.Equals(value, "Accept-Encoding", StringComparison.OrdinalIgnoreCase));
        Assert.True(compressed.Headers.CacheControl?.NoStore == true);
        var encoded = await compressed.Content.ReadAsByteArrayAsync();
        Assert.True(encoded.Length < plainBytes.Length);
        using var gzip = new GZipStream(new MemoryStream(encoded), CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        await gzip.CopyToAsync(decoded);
        Assert.Equal(plainBytes, decoded.ToArray());
    }

    [Theory]
    [InlineData("GET", Catalog + "?q=fixture", "gzip")]
    [InlineData("POST", Catalog, "gzip")]
    [InlineData("GET", "/api/public/storefront/orders/1/payment-status", "gzip")]
    [InlineData("GET", "/api/customers", "gzip")]
    [InlineData("GET", "/hubs/orders", "gzip")]
    [InlineData("GET", Catalog, "gzip;q=0, identity")]
    [InlineData("GET", Catalog, "unsupported")]
    public async Task No_compression_for_other_routes_methods_or_unsupported_encoding(string method, string path, string encoding)
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.AcceptEncoding.ParseAdd(encoding);
        using var response = await client.SendAsync(request);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(Payload, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rejection_before_compression_is_not_encoded()
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        using var request = new HttpRequestMessage(HttpMethod.Get, Catalog);
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        request.Headers.Add("X-Deny-Fixture", "1");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
