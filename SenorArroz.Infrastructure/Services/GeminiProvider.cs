using System.Text.Json;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using System.Net.Http.Json;

namespace SenorArroz.Infrastructure.Services;

public class GeminiProvider : IAiProvider, IAiChatProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiProvider> _logger;

    public GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Provider => "gemini";
    public string ProviderName => Provider;
    public async Task<AiChatResponse> GenerateAsync(AiChatRequest input, CancellationToken cancellationToken = default)
    {
        var uri=$"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(input.Model)}:generateContent?key={Uri.EscapeDataString(input.ApiKey)}";
        var contents=input.Messages.Where(x=>x.Role!="system").Select(x=>new { role=x.Role=="assistant"?"model":"user", parts=new[]{new { text=x.Content ?? string.Empty }} });
        try { using var response=await _httpClient.PostAsJsonAsync(uri,new { system_instruction=new { parts=new[]{new { text=input.Messages.FirstOrDefault(x=>x.Role=="system")?.Content ?? string.Empty }}}, contents, generationConfig=new { temperature=input.Temperature }},cancellationToken); var body=await response.Content.ReadAsStringAsync(cancellationToken); if(!response.IsSuccessStatusCode)return new(null,[],input.Model,null,null,null,(int)response.StatusCode is 429 or >=500,$"Gemini HTTP {(int)response.StatusCode}"); using var doc=JsonDocument.Parse(body); var text=doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString(); return new(text,[],input.Model,"stop",null,null); } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException){return new(null,[],input.Model,null,null,null,true,"Gemini no disponible temporalmente.");}
    }

    public async Task<AiModelProviderResult> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var uri = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new AiModelProviderResult(false, [], ClassifyError(body, (int)response.StatusCode));

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("models", out var data) || data.ValueKind != JsonValueKind.Array)
                return new AiModelProviderResult(true, [], null);

            var models = data.EnumerateArray()
                .Where(SupportsContentGeneration)
                .Select(ToModel)
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AiModelProviderResult(true, models, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemini model listing timed out.");
            return new AiModelProviderResult(false, [], "Google Gemini no respondio a tiempo. Intenta nuevamente.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Gemini model listing failed.");
            return new AiModelProviderResult(false, [], "No se pudieron obtener modelos de Google Gemini. Verifica conexion o disponibilidad del proveedor.");
        }
    }

    private static AiProviderModel ToModel(JsonElement element)
    {
        var name = AiProviderJson.TryGetString(element, "name") ?? string.Empty;
        var id = name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? name["models/".Length..]
            : name;
        var displayName = AiProviderJson.TryGetString(element, "displayName");

        return new AiProviderModel(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName);
    }

    private static bool SupportsContentGeneration(JsonElement element)
    {
        if (!element.TryGetProperty("supportedGenerationMethods", out var methods) || methods.ValueKind != JsonValueKind.Array)
            return true;

        return methods.EnumerateArray()
            .Any(x => x.ValueKind == JsonValueKind.String
                && string.Equals(x.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase));
    }

    private static string ClassifyError(string body, int statusCode)
    {
        var providerMessage = AiProviderJson.ExtractProviderError(body, $"Gemini respondio con HTTP {statusCode}.");
        return statusCode is 400 or 401 or 403
            ? $"API Key invalida o sin permisos para Google Gemini. {providerMessage}"
            : providerMessage;
    }
}
