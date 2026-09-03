using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public sealed class MetaConversionsClientTests
{
    [Fact]
    public async Task Purchase_uses_same_dedup_id_and_hashed_matching_data()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"events_received\":1,\"messages\":[]}");
        var client = CreateClient(handler, "TEST94728");

        await client.SendPurchaseAsync(new MetaPurchaseEvent(
            54321,
            new DateTime(2026, 9, 3, 21, 0, 0, DateTimeKind.Utc),
            "3001234567",
            40_000,
            3_000,
            1,
            "online",
            [new MetaPurchaseContent(27, 2), new MetaPurchaseContent(35, 1)],
            "Mozilla/5.0 test-browser",
            "203.0.113.20",
            "fb.1.1725397200000.123456789",
            "fb.1.1725397200000.AbCdEf"),
            CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "https://graph.facebook.com/v25.0/1941546679814779/events",
            handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal("server-token", handler.Request.Headers.Authorization?.Parameter);

        var raw = handler.Body!;
        Assert.DoesNotContain("3001234567", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("573001234567", raw, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        Assert.Equal("TEST94728", root.GetProperty("test_event_code").GetString());
        var data = root.GetProperty("data")[0];
        Assert.Equal("Purchase", data.GetProperty("event_name").GetString());
        Assert.Equal("purchase-54321", data.GetProperty("event_id").GetString());
        Assert.Equal("website", data.GetProperty("action_source").GetString());
        Assert.Equal("https://senorarroz.com", data.GetProperty("event_source_url").GetString());

        var userData = data.GetProperty("user_data");
        Assert.Equal(Sha256("573001234567"), userData.GetProperty("ph")[0].GetString());
        Assert.Equal("Mozilla/5.0 test-browser", userData.GetProperty("client_user_agent").GetString());
        Assert.Equal("203.0.113.20", userData.GetProperty("client_ip_address").GetString());
        Assert.Equal("fb.1.1725397200000.123456789", userData.GetProperty("fbp").GetString());
        Assert.Equal("fb.1.1725397200000.AbCdEf", userData.GetProperty("fbc").GetString());

        var custom = data.GetProperty("custom_data");
        Assert.Equal("COP", custom.GetProperty("currency").GetString());
        Assert.Equal(40_000m, custom.GetProperty("value").GetDecimal());
        Assert.Equal(3_000, custom.GetProperty("shipping").GetInt32());
        Assert.Equal("54321", custom.GetProperty("transaction_id").GetString());
        Assert.Equal(54321, custom.GetProperty("order_id").GetInt32());
        Assert.Equal("online", custom.GetProperty("payment_type").GetString());
        Assert.Equal(3, custom.GetProperty("num_items").GetInt32());
        Assert.Equal(new[] { "27", "35" }, custom.GetProperty("content_ids").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task Purchase_fails_when_meta_rejects_event()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid parameter\"}}");
        var client = CreateClient(handler, null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendPurchaseAsync(new MetaPurchaseEvent(
            88,
            DateTime.UtcNow,
            "3001234567",
            10_000,
            0,
            1,
            "cash",
            [new MetaPurchaseContent(27, 1)],
            "Mozilla/5.0",
            null,
            null,
            null), CancellationToken.None));

        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
    }

    private static MetaConversionsClient CreateClient(RecordingHandler handler, string? testCode)
    {
        var options = Options.Create(new MetaConversionsOptions
        {
            PixelId = "1941546679814779",
            AccessToken = "server-token",
            GraphApiVersion = "v25.0",
            EventSourceUrl = "https://senorarroz.com",
            TestEventCode = testCode,
        });
        return new MetaConversionsClient(new HttpClient(handler), options);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
