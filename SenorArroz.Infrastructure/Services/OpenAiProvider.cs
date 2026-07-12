using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;

namespace SenorArroz.Infrastructure.Services;

public class OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger) : IAiProvider, IAiChatProvider
{
    private static readonly Regex ToolName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    public string Provider => "openai";
    public string ProviderName => Provider;

    public async Task<AiChatResponse> GenerateAsync(AiChatRequest input, CancellationToken cancellationToken = default)
    {
        if (!SupportsChatCompletions(input.Model))
            return Failure(input, false, $"El modelo '{input.Model}' no es compatible con /v1/chat/completions y tool calling.");

        var toolError = ValidateTools(input.Tools);
        if (toolError != null)
        {
            logger.LogError("OpenAI tool validation failed Tool={ToolName} Error={ToolError}", toolError.Value.Name, toolError.Value.Error);
            return Failure(input, false, $"Herramienta '{toolError.Value.Name}' inválida: {toolError.Value.Error}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", input.ApiKey);
        request.Content = new StringContent(BuildRequestJson(input), Encoding.UTF8, "application/json");
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var providerError = AiProviderJson.ExtractProviderError(body, $"OpenAI respondió con HTTP {(int)response.StatusCode}.");
                logger.LogError("OpenAI request failed StatusCode={StatusCode} ResponseBody={ResponseBody} ProviderError={ProviderError}", (int)response.StatusCode, body, providerError);
                return Failure(input, (int)response.StatusCode is 408 or 409 or 429 or >= 500, providerError);
            }

            logger.LogInformation("OpenAI request completed StatusCode={StatusCode} Model={Model}", (int)response.StatusCode, input.Model);
            using var doc = JsonDocument.Parse(body);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var msg = choice.GetProperty("message");
            var text = msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null;
            var calls = new List<AiToolCall>();
            if (msg.TryGetProperty("tool_calls", out var toolCalls))
                foreach (var item in toolCalls.EnumerateArray())
                {
                    var function = item.GetProperty("function");
                    using var args = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "{}");
                    calls.Add(new(item.GetProperty("id").GetString()!, function.GetProperty("name").GetString()!, args.RootElement.Clone()));
                }
            var usage = doc.RootElement.TryGetProperty("usage", out var u) ? u : default;
            return new(text, calls, input.Model, choice.GetProperty("finish_reason").GetString(), usage.ValueKind != JsonValueKind.Undefined ? usage.GetProperty("prompt_tokens").GetInt32() : null, usage.ValueKind != JsonValueKind.Undefined ? usage.GetProperty("completion_tokens").GetInt32() : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "OpenAI request technical failure Model={Model}", input.Model);
            return Failure(input, true, ex.Message);
        }
    }

    internal static string BuildRequestJson(AiChatRequest input)
    {
        var root = new JsonObject { ["model"] = input.Model };
        if (input.Temperature.HasValue && SupportsTemperature(input.Model)) root["temperature"] = input.Temperature.Value;
        var messages = new JsonArray();
        foreach (var message in input.Messages)
        {
            var node = new JsonObject { ["role"] = message.Role, ["content"] = message.Content };
            if (message.Role == "assistant" && message.ToolCalls is { Count: > 0 })
                node["tool_calls"] = new JsonArray(message.ToolCalls.Select(call => (JsonNode)new JsonObject { ["id"] = call.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.GetRawText() } }).ToArray());
            if (message.Role == "tool") node["tool_call_id"] = message.ToolCallId;
            messages.Add(node);
        }
        root["messages"] = messages;
        if (input.Tools.Count > 0)
            root["tools"] = new JsonArray(input.Tools.Select(tool => (JsonNode)new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = JsonNode.Parse(tool.ParametersSchema.GetRawText()) } }).ToArray());
        return root.ToJsonString(new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    }

    private static (string Name, string Error)? ValidateTools(IReadOnlyList<AiToolDefinition> tools)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (!ToolName.IsMatch(tool.Name)) return (tool.Name, "nombre no válido");
            if (!names.Add(tool.Name)) return (tool.Name, "nombre duplicado");
            if (tool.ParametersSchema.ValueKind != JsonValueKind.Object) return (tool.Name, "parameters debe ser un objeto JSON");
            if (!tool.ParametersSchema.TryGetProperty("type", out var type) || type.GetString() != "object") return (tool.Name, "la raíz debe tener type=object");
            if (tool.ParametersSchema.TryGetProperty("properties", out var properties) && properties.ValueKind != JsonValueKind.Object) return (tool.Name, "properties debe ser un objeto");
            if (tool.ParametersSchema.TryGetProperty("required", out var required))
            {
                if (required.ValueKind != JsonValueKind.Array) return (tool.Name, "required debe ser un arreglo");
                foreach (var item in required.EnumerateArray())
                    if (item.ValueKind != JsonValueKind.String || !tool.ParametersSchema.TryGetProperty("properties", out properties) || !properties.TryGetProperty(item.GetString()!, out _)) return (tool.Name, $"required contiene la propiedad inexistente '{item}'");
            }
        }
        return null;
    }

    private static bool SupportsChatCompletions(string model) => model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(model, "^o[1-9]", RegexOptions.IgnoreCase);
    private static bool SupportsTemperature(string model) => !Regex.IsMatch(model, "^(o[1-9]|gpt-5)", RegexOptions.IgnoreCase);
    private static AiChatResponse Failure(AiChatRequest input, bool transient, string error) => new(null, [], input.Model, null, null, null, transient, error);

    public async Task<AiModelProviderResult> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, [], AiProviderJson.ExtractProviderError(body, $"OpenAI respondió con HTTP {(int)response.StatusCode}."));
            using var document = JsonDocument.Parse(body);
            var models = document.RootElement.GetProperty("data").EnumerateArray().Select(x => AiProviderJson.TryGetString(x, "id")).Where(x => x != null && SupportsChatCompletions(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Select(x => new AiProviderModel(x!, x!)).ToList();
            return new(true, models, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { logger.LogWarning(ex, "OpenAI model listing failed"); return new(false, [], ex.Message); }
    }
}
