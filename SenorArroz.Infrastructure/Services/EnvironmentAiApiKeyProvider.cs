using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public sealed class EnvironmentAiApiKeyProvider : IAiApiKeyProvider
{
    public string? GetApiKey(string provider)
    {
        var value = Environment.GetEnvironmentVariable(GetEnvironmentVariableName(provider));
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string GetEnvironmentVariableName(string provider) => Normalize(provider) switch
    {
        "openai" => "OPENAI_API_KEY",
        "gemini" => "GEMINI_API_KEY",
        _ => throw new ArgumentException($"Proveedor de IA no soportado: {provider}.", nameof(provider))
    };

    private static string Normalize(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "google_gemini" or "google-gemini" or "google gemini" ? "gemini" : value;
    }
}
