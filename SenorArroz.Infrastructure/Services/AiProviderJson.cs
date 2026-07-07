using System.Text.Json;

namespace SenorArroz.Infrastructure.Services;

internal static class AiProviderJson
{
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
}
