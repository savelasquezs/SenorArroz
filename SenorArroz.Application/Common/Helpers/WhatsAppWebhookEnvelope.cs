using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SenorArroz.Application.Common.Services;

namespace SenorArroz.Application.Common.Helpers;

public sealed record WhatsAppWebhookEnvelope(string Key, string Partition, string Payload,
    string PhoneNumberId, string? PhoneNumber, string? UserId, string[] Aliases)
{
    public static IReadOnlyList<WhatsAppWebhookEnvelope> Split(string rawPayload)
    {
        using var document = JsonDocument.Parse(rawPayload);
        var result = new List<WhatsAppWebhookEnvelope>();
        if (!document.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                var phoneId = value.TryGetProperty("metadata", out var metadata) ? Read(metadata, "phone_number_id") : null;
                if (string.IsNullOrEmpty(phoneId)) continue;
                foreach (var property in new[] { "messages", "statuses" })
                {
                    if (!value.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        var payloadValue = value.EnumerateObject()
                            .Where(x => x.Name is not ("messages" or "statuses"))
                            .ToDictionary(x => x.Name, x => (object)x.Value.Clone());
                        payloadValue[property] = new[] { item.Clone() };
                        var payload = JsonSerializer.Serialize(new
                        {
                            @object = "whatsapp_business_account",
                            entry = new[] { new { id = Read(entry, "id"), changes = new[] {
                                new { field = Read(change, "field"), value = payloadValue } } } }
                        });
                        var identity = property == "messages" ? WhatsAppWebhookIdentityReader.Read(value, item)
                            : new WhatsAppInboundIdentity(Read(item, "recipient_id"), Read(item, "recipient_user_id"), null, null);
                        var aliases = AliasesFor(phoneId, identity.PhoneNumber, identity.UserId);
                        var id = Read(item, "id");
                        var key = string.IsNullOrWhiteSpace(id) ? Hash(payload)
                            : Hash($"{phoneId}:{property}:{id}:{Read(item, "status")}:{Read(item, "timestamp")}");
                        result.Add(new(key, aliases.FirstOrDefault() ?? Hash($"{phoneId}:unknown"), payload,
                            phoneId, identity.PhoneNumber, identity.UserId, aliases));
                    }
                }
            }
        }
        return result;
    }

    public static string[] AliasesFor(string phoneId, string? phone, string? userId) => new[]
        {
            string.IsNullOrWhiteSpace(phone) ? null : Hash($"{phoneId}:phone:{phone}"),
            string.IsNullOrWhiteSpace(userId) ? null : Hash($"{phoneId}:user:{userId}")
        }.OfType<string>().Distinct().ToArray();

    private static string? Read(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
