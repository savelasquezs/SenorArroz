using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.Application.Common.Models;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class GeminiProviderTests
{
    [Fact]
    public async Task Http404_PreservesProviderErrorMessage()
    {
        var handler = new CaptureHandler(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"model is no longer available","status":"NOT_FOUND"}}""");
        var result = await new GeminiProvider(new HttpClient(handler), NullLogger<GeminiProvider>.Instance)
            .GenerateAsync(new("gemini-old", "secret", [new("user", "Hola")], [], null));
        Assert.Equal("model is no longer available", result.Error);
        Assert.Equal(404, result.HttpStatusCode);
        Assert.False(result.IsTransientError);
    }

    [Fact]
    public async Task ToolCall_IsParsed_AndRoundTripIncludesFunctionResponseAndSignature()
    {
        var response = """{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"search_products","args":{"query":"arroz"},"id":"call_1"},"thoughtSignature":"signature"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":4,"candidatesTokenCount":2}}""";
        var firstHandler = new CaptureHandler(HttpStatusCode.OK, response);
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}},"required":["query"],"additionalProperties":false}""");
        var request = new AiChatRequest("gemini-flash-latest", "secret", [new("user", "Qué venden")], [new("search_products", "Busca productos", schema.RootElement.Clone())], 0.3);
        var first = await new GeminiProvider(new HttpClient(firstHandler), NullLogger<GeminiProvider>.Instance).GenerateAsync(request);
        var call = Assert.Single(first.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("signature", call.ProviderMetadata);

        var secondHandler = new CaptureHandler(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"Vendemos arroz"}]},"finishReason":"STOP"}]}""");
        var messages = new List<AiChatMessage> { new("user", "Qué venden"), new("assistant", null, null, first.ToolCalls), new("tool", "{\"success\":true}", call.Id) };
        await new GeminiProvider(new HttpClient(secondHandler), NullLogger<GeminiProvider>.Instance).GenerateAsync(request with { Messages = messages });
        using var body = JsonDocument.Parse(secondHandler.Body!);
        var contents = body.RootElement.GetProperty("contents");
        Assert.Equal("signature", contents[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        var functionResponse = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal("search_products", functionResponse.GetProperty("name").GetString());
        Assert.Equal("call_1", functionResponse.GetProperty("id").GetString());
        Assert.True(body.RootElement.TryGetProperty("tools", out _));
        Assert.DoesNotContain("additionalProperties", secondHandler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidToolSchema_IdentifiesToolWithoutCallingGemini()
    {
        var handler = new CaptureHandler(
            HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"OK"}]}}]}""");
        using var schema = JsonDocument.Parse(
            """{"type":"array","items":{"type":"string"}}""");
        var provider = new GeminiProvider(new HttpClient(handler), NullLogger<GeminiProvider>.Instance);

        var result = await provider.GenerateAsync(new(
            "gemini-flash-latest",
            "secret",
            [new("user", "Hola")],
            [new("search_products", "Busca", schema.RootElement.Clone())],
            null));

        Assert.Contains("search_products", result.Error);
        Assert.Contains("type=object", result.Error);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class CaptureHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public int CallCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
