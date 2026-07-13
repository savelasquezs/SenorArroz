using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
        Assert.Equal(400, result.HttpStatusCode);
        Assert.False(result.IsTransientError);
    }

    [Fact]
    public async Task HttpError_RedactsApiKeyFromReturnedErrorAndLogs()
    {
        const string apiKey = "sk-proj-super-secret-value";
        var handler = new CaptureHandler(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided: sk-proj-super-secret-value"},"api_key":"sk-proj-super-secret-value"}""");
        var logger = new RecordingLogger<OpenAiProvider>();
        var provider = new OpenAiProvider(new HttpClient(handler), logger);

        var result = await provider.GenerateAsync(new("gpt-4o-mini", apiKey, [new("user", "Hola")], [], null));

        Assert.DoesNotContain(apiKey, result.Error);
        Assert.Contains("[REDACTED]", result.Error);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(apiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChatLatest_IsCheckedByTheRealEndpointInsteadOfANamePrefix()
    {
        var handler = new CaptureHandler(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"OK"},"finish_reason":"stop"}]}""");
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{},"additionalProperties":false}""");
        var provider = new OpenAiProvider(new HttpClient(handler), NullLogger<OpenAiProvider>.Instance);

        var result = await provider.GenerateAsync(new(
            "chat-latest",
            "secret",
            [new("user", "OK")],
            [new("compatibility_probe", "probe", schema.RootElement.Clone())],
            null));

        Assert.Null(result.Error);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("compatibility_probe", handler.Body);
    }

    [Fact]
    public async Task InvalidToolSchema_IdentifiesToolWithoutCallingOpenAi()
    {
        var handler = new CaptureHandler(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"OK"},"finish_reason":"stop"}]}""");
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["missing"]}""");
        var provider = new OpenAiProvider(new HttpClient(handler), NullLogger<OpenAiProvider>.Instance);

        var result = await provider.GenerateAsync(new(
            "gpt-4o-mini",
            "secret",
            [new("user", "Hola")],
            [new("search_products", "Busca", schema.RootElement.Clone())],
            null));

        Assert.Contains("search_products", result.Error);
        Assert.Contains("missing", result.Error);
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
