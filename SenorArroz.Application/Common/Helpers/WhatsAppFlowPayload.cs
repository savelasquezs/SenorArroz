using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppFlowPayload
{
    public static string WithoutTokens(string json)
    {
        var node = JsonNode.Parse(json);
        RemoveTokens(node);
        return node?.ToJsonString() ?? "{}";
    }

    public static string RestoreCompletionToken(string json, string token)
    {
        var node = JsonNode.Parse(json);
        if (node?["data"]?["extension_message_response"]?["params"] is JsonObject parameters)
            parameters["flow_token"] = token;
        return node?.ToJsonString() ?? "{}";
    }

    public static int? Integer(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }

    private static void RemoveTokens(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("flow_token");
            foreach (var property in obj.ToArray())
            {
                if (property.Key == "response_json" && property.Value is JsonValue value && value.TryGetValue<string>(out var nested))
                {
                    try { obj[property.Key] = WithoutTokens(nested); }
                    catch (JsonException) { obj[property.Key] = "{}"; }
                }
                else RemoveTokens(property.Value);
            }
        }
        else if (node is JsonArray array)
            foreach (var item in array) RemoveTokens(item);
    }
}
