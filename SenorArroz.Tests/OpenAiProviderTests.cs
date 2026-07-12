using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.Application.Common.Models;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class OpenAiProviderTests
{
    [Fact]
    public async Task NormalMessages_OmitToolFields_AndToolMessageIncludesCallId()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""");
        var provider = new OpenAiProvider(new HttpClient(handler), NullLogger<OpenAiProvider>.Instance);
        await provider.GenerateAsync(new("gpt-4o-mini", "secret", [new("system", "s"), new("user", "Hola"), new("tool", "{}", "call_1")], [], null));

        using var json = JsonDocument.Parse(handler.Body!);
        var messages = json.RootElement.GetProperty("messages");
        Assert.False(messages[0].TryGetProperty("tool_call_id", out _));
        Assert.False(messages[0].TryGetProperty("tool_calls", out _));
        Assert.False(messages[1].TryGetProperty("tool_call_id", out _));
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
        Assert.False(json.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Http400_PreservesProviderErrorBodyMessage()
    {
        var handler = new CaptureHandler(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid parameter: tool_call_id"}}""");
        var provider = new OpenAiProvider(new HttpClient(handler), NullLogger<OpenAiProvider>.Instance);
        var result = await provider.GenerateAsync(new("gpt-4o-mini", "secret", [new("user", "Hola")], [], null));
        Assert.Contains("Invalid parameter: tool_call_id", result.Error);
        Assert.False(result.IsTransientError);
    }

    private sealed class CaptureHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
