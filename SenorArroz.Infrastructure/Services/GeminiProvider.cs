using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;

namespace SenorArroz.Infrastructure.Services;

public class GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger) : IAiProvider, IAiChatProvider
{
    public string Provider => "gemini";
    public string ProviderName => Provider;

    public async Task<AiChatResponse> GenerateAsync(AiChatRequest input, CancellationToken cancellationToken = default)
    {
        var uri = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(input.Model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("x-goog-api-key", input.ApiKey);
        request.Content = new StringContent(BuildRequestJson(input), Encoding.UTF8, "application/json");

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var providerError = AiProviderJson.ExtractProviderError(body, $"Gemini respondió con HTTP {(int)response.StatusCode}.");
                logger.LogError("Gemini request failed StatusCode={StatusCode} ResponseBody={ResponseBody} ProviderError={ProviderError} Model={Model}", (int)response.StatusCode, body, providerError, input.Model);
                return Failure(input, (int)response.StatusCode is 408 or 409 or 429 or >= 500, providerError);
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                var feedback = document.RootElement.TryGetProperty("promptFeedback", out var promptFeedback) ? promptFeedback.GetRawText() : "Sin candidatos ni promptFeedback.";
                logger.LogError("Gemini returned no candidates Model={Model} PromptFeedback={PromptFeedback}", input.Model, feedback);
                return Failure(input, false, $"Gemini no devolvió candidatos. {feedback}");
            }

            var candidate = candidates[0];
            var parts = candidate.GetProperty("content").GetProperty("parts");
            var textParts = new List<string>();
            var calls = new List<AiToolCall>();
            var index = 0;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString())) textParts.Add(text.GetString()!);
                if (!part.TryGetProperty("functionCall", out var functionCall)) continue;
                var name = functionCall.GetProperty("name").GetString() ?? string.Empty;
                var id = functionCall.TryGetProperty("id", out var callId) && callId.ValueKind == JsonValueKind.String ? callId.GetString()! : $"gemini-{name}-{index++}";
                var args = functionCall.TryGetProperty("args", out var arguments) ? arguments.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
                var metadata = part.TryGetProperty("thoughtSignature", out var signature) && signature.ValueKind == JsonValueKind.String ? signature.GetString() : null;
                calls.Add(new(id, name, args, metadata));
            }

            var usage = document.RootElement.TryGetProperty("usageMetadata", out var usageMetadata) ? usageMetadata : default;
            var inputTokens = TryGetInt(usage, "promptTokenCount");
            var outputTokens = TryGetInt(usage, "candidatesTokenCount");
            var finishReason = candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() : null;
            logger.LogInformation("Gemini request completed StatusCode={StatusCode} Model={Model} ToolCallCount={ToolCallCount}", (int)response.StatusCode, input.Model, calls.Count);
            return new(string.Join("\n", textParts), calls, input.Model, finishReason, inputTokens, outputTokens);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogError(ex, "Gemini request technical failure Model={Model}", input.Model);
            return Failure(input, true, ex.Message);
        }
    }

    internal static string BuildRequestJson(AiChatRequest input)
    {
        var root = new JsonObject();
        var system = input.Messages.FirstOrDefault(x => x.Role == "system")?.Content;
        if (!string.IsNullOrWhiteSpace(system)) root["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) };

        var callNames = input.Messages.SelectMany(x => x.ToolCalls ?? []).ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);
        var contents = new JsonArray();
        foreach (var message in input.Messages.Where(x => x.Role != "system"))
        {
            var parts = new JsonArray();
            if (message.Role == "assistant" && message.ToolCalls is { Count: > 0 })
            {
                foreach (var call in message.ToolCalls)
                {
                    var part = new JsonObject { ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = JsonNode.Parse(call.Arguments.GetRawText()), ["id"] = call.Id } };
                    if (!string.IsNullOrWhiteSpace(call.ProviderMetadata)) part["thoughtSignature"] = call.ProviderMetadata;
                    parts.Add(part);
                }
            }
            else if (message.Role == "tool")
            {
                var name = message.ToolCallId != null && callNames.TryGetValue(message.ToolCallId, out var toolName) ? toolName : message.ToolCallId ?? "unknown_tool";
                JsonNode response;
                try { response = JsonNode.Parse(message.Content ?? "{}") ?? new JsonObject(); }
                catch (JsonException) { response = new JsonObject { ["result"] = message.Content }; }
                parts.Add(new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = name, ["id"] = message.ToolCallId, ["response"] = response } });
            }
            else parts.Add(new JsonObject { ["text"] = message.Content ?? string.Empty });
            contents.Add(new JsonObject { ["role"] = message.Role == "assistant" ? "model" : "user", ["parts"] = parts });
        }
        root["contents"] = contents;

        if (input.Tools.Count > 0)
            root["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = new JsonArray(input.Tools.Select(tool => (JsonNode)new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = BuildGeminiSchema(tool.ParametersSchema) }).ToArray()) });
        if (input.Temperature.HasValue) root["generationConfig"] = new JsonObject { ["temperature"] = input.Temperature.Value };
        return root.ToJsonString(new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    }

    private static JsonNode BuildGeminiSchema(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText()) ?? new JsonObject();
        RemoveUnsupportedSchemaKeywords(node);
        return node;
    }

    private static void RemoveUnsupportedSchemaKeywords(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            // Gemini function declarations use a Schema/OpenAPI subset and reject the JSON Schema
            // keyword additionalProperties even when its value is false.
            jsonObject.Remove("additionalProperties");
            foreach (var child in jsonObject.ToList())
                if (child.Value is not null) RemoveUnsupportedSchemaKeywords(child.Value);
        }
        else if (node is JsonArray jsonArray)
            foreach (var child in jsonArray)
                if (child is not null) RemoveUnsupportedSchemaKeywords(child);
    }

    public async Task<AiModelProviderResult> ListModelsAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var uri = "https://generativelanguage.googleapis.com/v1beta/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.Add("x-goog-api-key", apiKey);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken); var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return new(false, [], AiProviderJson.ExtractProviderError(body, $"Gemini respondió con HTTP {(int)response.StatusCode}."));
            using var document = JsonDocument.Parse(body);
            var models = document.RootElement.GetProperty("models").EnumerateArray().Where(SupportsContentGeneration).Select(ToModel).Where(x => !string.IsNullOrWhiteSpace(x.Id)).DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
            return new(true, models, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { logger.LogWarning(ex, "Gemini model listing failed"); return new(false, [], ex.Message); }
    }

    private static AiProviderModel ToModel(JsonElement element) { var name = AiProviderJson.TryGetString(element, "name") ?? string.Empty; var id = name.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? name[7..] : name; return new(id, AiProviderJson.TryGetString(element, "displayName") ?? id); }
    private static bool SupportsContentGeneration(JsonElement element) => !element.TryGetProperty("supportedGenerationMethods", out var methods) || methods.ValueKind != JsonValueKind.Array || methods.EnumerateArray().Any(x => x.GetString() == "generateContent");
    private static int? TryGetInt(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static AiChatResponse Failure(AiChatRequest input, bool transient, string error) => new(null, [], input.Model, null, null, null, transient, error);
}
