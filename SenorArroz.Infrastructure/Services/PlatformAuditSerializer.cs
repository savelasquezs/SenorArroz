using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenorArroz.Infrastructure.Services;

internal static class PlatformAuditSerializer
{
    public static string Serialize(object? value)
    {
        if (value is null) return "null";
        var node = JsonSerializer.SerializeToNode(value, new JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        });
        Redact(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToList())
            {
                if (IsSensitive(key)) obj[key] = "[REDACTED]";
                else Redact(obj[key]);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) Redact(item);
        }
    }

    private static bool IsSensitive(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("hash", StringComparison.OrdinalIgnoreCase)
        || key.Contains("credential", StringComparison.OrdinalIgnoreCase);
}
