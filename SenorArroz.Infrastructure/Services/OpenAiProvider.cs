using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;

namespace SenorArroz.Infrastructure.Services;

public class OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger) : IAiProvider, IAiChatProvider
{
    public string Provider => "openai";
    public string ProviderName => Provider;

    public async Task<AiChatResponse> GenerateAsync(AiChatRequest input, CancellationToken cancellationToken = default)
    {
        var toolError = AiToolDefinitionValidator.Validate(input.Tools);
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
                var safeBody = AiProviderJson.SanitizeProviderPayload(body, input.ApiKey);
                var providerError = AiProviderJson.ExtractProviderError(safeBody, $"OpenAI respondió con HTTP {(int)response.StatusCode}.");
                logger.LogError("OpenAI request failed StatusCode={StatusCode} ResponseBody={ResponseBody} ProviderError={ProviderError}", (int)response.StatusCode, safeBody, providerError);
                return Failure(input, (int)response.StatusCode is 408 or 409 or 429 or >= 500, providerError, (int)response.StatusCode);
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
            var inputTokens = TryGetInt(usage, "prompt_tokens");
            var outputTokens = TryGetInt(usage, "completion_tokens");
            int? cachedTokens = null;
            int? thinkingTokens = null;
            if (usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails)) cachedTokens = TryGetInt(promptDetails, "cached_tokens");
                if (usage.TryGetProperty("completion_tokens_details", out var completionDetails)) thinkingTokens = TryGetInt(completionDetails, "reasoning_tokens");
            }
            return new(text, calls, input.Model, choice.GetProperty("finish_reason").GetString(), inputTokens, outputTokens, CachedInputTokens: cachedTokens, ThinkingTokens: thinkingTokens);
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

    private static bool IsPotentialConversationModel(string model) =>
        model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("chat-", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(model, "^o[1-9]", RegexOptions.IgnoreCase);
    private static bool SupportsTemperature(string model) => !Regex.IsMatch(model, "^(o[1-9]|gpt-5)", RegexOptions.IgnoreCase);
    private static int? TryGetInt(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
    private static AiChatResponse Failure(AiChatRequest input, bool transient, string error, int? httpStatusCode = null) => new(null, [], input.Model, null, null, null, transient, error, httpStatusCode);

    public async Task<AiModelProviderResult> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var safeBody = AiProviderJson.SanitizeProviderPayload(body, apiKey);
                return new(false, [], AiProviderJson.ExtractProviderError(safeBody, $"OpenAI respondió con HTTP {(int)response.StatusCode}."));
            }
            using var document = JsonDocument.Parse(body);
            // /models has no reliable capability metadata. This is only a broad
            // candidate list; TestConnection performs the authoritative request
            // to chat/completions with a tool definition.
            var models = document.RootElement.GetProperty("data").EnumerateArray().Select(x => AiProviderJson.TryGetString(x, "id")).Where(x => x != null && IsPotentialConversationModel(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Select(x => new AiProviderModel(x!, x!)).ToList();
            return new(true, models, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { logger.LogWarning(ex, "OpenAI model listing failed"); return new(false, [], ex.Message); }
    }
}
