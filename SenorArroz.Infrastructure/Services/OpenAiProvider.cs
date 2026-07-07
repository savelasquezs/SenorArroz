using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Provider => "openai";

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
