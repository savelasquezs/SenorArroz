using System.Text.Json;
using System.Text.RegularExpressions;

namespace SenorArroz.Infrastructure.Services;

internal static class AiProviderJson
{
    private static readonly Regex BearerToken = new(@"(?i)Bearer\s+[A-Za-z0-9._~+\-/=]+", RegexOptions.Compiled);
    private static readonly Regex QuerySecret = new(@"(?i)([?&](?:key|api_key|apikey|access_token)=)[^&\s]+", RegexOptions.Compiled);
    private static readonly Regex JsonSecret = new("(?i)(\\\"(?:apiKey|api_key|accessToken|access_token|authorization)\\\"\\s*:\\s*\\\")[^\\\"]+(\\\")", RegexOptions.Compiled);
    private static readonly Regex OpenAiKey = new(@"(?i)\bsk-(?:proj-)?[A-Za-z0-9_-]{8,}", RegexOptions.Compiled);
    private static readonly Regex GeminiKey = new(@"\bAIza[A-Za-z0-9_-]{12,}", RegexOptions.Compiled);
    private static readonly Regex ApiKeyValue = new(@"(?i)(api\s*key(?:\s+provided)?\s*[:=]\s*)[^\s,;""'{}\[\]]+", RegexOptions.Compiled);

    public static string ExtractProviderError(string body, string fallback)
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

    public static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string SanitizeProviderPayload(string? value, string? configuredSecret = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value;
        if (!string.IsNullOrWhiteSpace(configuredSecret))
            sanitized = sanitized.Replace(configuredSecret, "[REDACTED]", StringComparison.Ordinal);
        sanitized = BearerToken.Replace(sanitized, "Bearer [REDACTED]");
        sanitized = QuerySecret.Replace(sanitized, "$1[REDACTED]");
        sanitized = JsonSecret.Replace(sanitized, "$1[REDACTED]$2");
        sanitized = OpenAiKey.Replace(sanitized, "[REDACTED]");
        sanitized = GeminiKey.Replace(sanitized, "[REDACTED]");
        return ApiKeyValue.Replace(sanitized, "$1[REDACTED]");
    }
}
