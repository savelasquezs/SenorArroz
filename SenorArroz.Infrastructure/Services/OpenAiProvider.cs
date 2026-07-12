using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using System.Net.Http.Json;

namespace SenorArroz.Infrastructure.Services;

public class OpenAiProvider : IAiProvider, IAiChatProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Provider => "openai";
    public string ProviderName => Provider;

    public async Task<AiChatResponse> GenerateAsync(AiChatRequest input, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", input.ApiKey);
        request.Content = JsonContent.Create(new { model = input.Model, temperature = input.Temperature, messages = input.Messages.Select(m => new { role = m.Role, content = m.Content, tool_call_id = m.ToolCallId, tool_calls = m.ToolCalls?.Select(t => new { id = t.Id, type = "function", function = new { name = t.Name, arguments = t.Arguments.GetRawText() } }) }), tools = input.Tools.Select(t => new { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.ParametersSchema } }) });
        try { using var response = await _httpClient.SendAsync(request, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken); if (!response.IsSuccessStatusCode) return new(null, [], input.Model, null, null, null, (int)response.StatusCode is 429 or >= 500, $"OpenAI HTTP {(int)response.StatusCode}"); using var doc = JsonDocument.Parse(body); var choice = doc.RootElement.GetProperty("choices")[0]; var msg = choice.GetProperty("message"); var text = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null; var calls = new List<AiToolCall>(); if (msg.TryGetProperty("tool_calls", out var tc)) foreach (var x in tc.EnumerateArray()) { var f=x.GetProperty("function"); using var args=JsonDocument.Parse(f.GetProperty("arguments").GetString() ?? "{}"); calls.Add(new(x.GetProperty("id").GetString()!, f.GetProperty("name").GetString()!, args.RootElement.Clone())); } var usage=doc.RootElement.TryGetProperty("usage",out var u)?u:default; return new(text,calls,input.Model,choice.GetProperty("finish_reason").GetString(),usage.ValueKind!=JsonValueKind.Undefined?usage.GetProperty("prompt_tokens").GetInt32():null,usage.ValueKind!=JsonValueKind.Undefined?usage.GetProperty("completion_tokens").GetInt32():null); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return new(null, [], input.Model, null, null, null, ex is HttpRequestException or TaskCanceledException, "OpenAI no disponible temporalmente."); }
    }

    public async Task<AiModelProviderResult> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new AiModelProviderResult(false, [], ClassifyError(body, (int)response.StatusCode));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new AiModelProviderResult(true, [], null);

            var models = data.EnumerateArray()
                .Select(x => AiProviderJson.TryGetString(x, "id"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => new AiProviderModel(x!, x!))
                .ToList();

            return new AiModelProviderResult(true, models, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "OpenAI model listing timed out.");
            return new AiModelProviderResult(false, [], "OpenAI no respondio a tiempo. Intenta nuevamente.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "OpenAI model listing failed.");
            return new AiModelProviderResult(false, [], "No se pudieron obtener modelos de OpenAI. Verifica conexion o disponibilidad del proveedor.");
        }
    }

    private static string ClassifyError(string body, int statusCode)
    {
        var providerMessage = AiProviderJson.ExtractProviderError(body, $"OpenAI respondio con HTTP {statusCode}.");
        return statusCode is 401 or 403
            ? $"API Key invalida o sin permisos para OpenAI. {providerMessage}"
            : providerMessage;
    }
}
