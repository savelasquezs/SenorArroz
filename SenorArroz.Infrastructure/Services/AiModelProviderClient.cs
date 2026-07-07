using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class AiModelProviderClient : IAiModelProviderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiModelProviderClient> _logger;

    public AiModelProviderClient(HttpClient httpClient, ILogger<AiModelProviderClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<AiModelProviderResult> ListModelsAsync(
        string provider,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProvider(provider);
        return normalized switch
        {
            "openai" => ListOpenAiModelsAsync(apiKey, cancellationToken),
            "gemini" => ListGeminiModelsAsync(apiKey, cancellationToken),
            _ => Task.FromResult(new AiModelProviderResult(false, [], "Proveedor de IA no soportado."))
        };
    }

    private async Task<AiModelProviderResult> ListOpenAiModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new AiModelProviderResult(false, [], ExtractProviderError(body, $"OpenAI respondio con HTTP {(int)response.StatusCode}."));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new AiModelProviderResult(true, [], null);

            var models = data.EnumerateArray()
                .Select(x => TryGetString(x, "id"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => new AiProviderModel(x!, x!))
                .ToList();

            return new AiModelProviderResult(true, models, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "OpenAI model listing failed.");
            return new AiModelProviderResult(false, [], "No se pudieron consultar los modelos de OpenAI.");
        }
    }

    private async Task<AiModelProviderResult> ListGeminiModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        var uri = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new AiModelProviderResult(false, [], ExtractProviderError(body, $"Gemini respondio con HTTP {(int)response.StatusCode}."));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("models", out var data) || data.ValueKind != JsonValueKind.Array)
                return new AiModelProviderResult(true, [], null);

            var models = data.EnumerateArray()
                .Where(SupportsGeminiContentGeneration)
                .Select(ToGeminiModel)
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AiModelProviderResult(true, models, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Gemini model listing failed.");
            return new AiModelProviderResult(false, [], "No se pudieron consultar los modelos de Google Gemini.");
        }
    }

    private static AiProviderModel ToGeminiModel(JsonElement element)
    {
        var name = TryGetString(element, "name") ?? string.Empty;
        var id = name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? name["models/".Length..]
            : name;
        var displayName = TryGetString(element, "displayName");

        return new AiProviderModel(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName);
    }

    private static bool SupportsGeminiContentGeneration(JsonElement element)
    {
        if (!element.TryGetProperty("supportedGenerationMethods", out var methods) || methods.ValueKind != JsonValueKind.Array)
            return true;

        return methods.EnumerateArray()
            .Any(x => x.ValueKind == JsonValueKind.String
                && string.Equals(x.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractProviderError(string body, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                    return TryGetString(error, "message") ?? fallback;
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
            // Fall through to fallback.
        }

        return fallback;
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "google_gemini" or "google-gemini" or "google gemini" ? "gemini" : value;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
