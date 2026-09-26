using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public sealed class MetaWebsiteConversionsClientTests
{
    [Fact]
    public async Task AddToCart_preserves_browser_event_id_for_deduplication()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"events_received":1}""");
        var client = CreateClient(handler);

        await client.SendAsync(new MetaWebsiteFunnelEvent(
            "add_to_cart",
            "add-to-cart-123",
            "/arroces/paisa",
            31_000,
            1,
            "delivery",
            null,
            "Mozilla/5.0 test-browser",
            "203.0.113.20",
            "fb.1.1725397200000.123456789",
            "fb.1.1725397200000.AbCdEf"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.Body!);
        var data = document.RootElement.GetProperty("data")[0];
        Assert.Equal("AddToCart", data.GetProperty("event_name").GetString());
        Assert.Equal("add-to-cart-123", data.GetProperty("event_id").GetString());
        Assert.Equal("https://senorarroz.com/arroces/paisa", data.GetProperty("event_source_url").GetString());
        Assert.Equal("Mozilla/5.0 test-browser", data.GetProperty("user_data").GetProperty("client_user_agent").GetString());
        Assert.Equal(31_000m, data.GetProperty("custom_data").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Unsupported_event_is_rejected_before_network_call()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"events_received":1}""");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<MetaConversionsException>(() => client.SendAsync(new MetaWebsiteFunnelEvent(
            "page_view",
            "page-view-1",
            "/",
            0,
            0,
            null,
            null,
            "Mozilla/5.0",
            null,
            null,
            null),
            CancellationToken.None));

        Assert.Null(handler.Body);
    }

    private static MetaWebsiteConversionsClient CreateClient(RecordingHandler handler)
    {
        var options = Options.Create(new MetaConversionsOptions
        {
            PixelId = "1941546679814779",
            AccessToken = "server-token",
            GraphApiVersion = "v25.0",
            EventSourceUrl = "https://senorarroz.com",
        });
        return new MetaWebsiteConversionsClient(new HttpClient(handler), options);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
