using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SenorArroz.Application.Common.Services;

namespace SenorArroz.Application.Common.Helpers;

public sealed record WhatsAppWebhookEnvelope(string Key, string Partition, string Payload,
    string PhoneNumberId, string? PhoneNumber, string? UserId, string[] Aliases)
{
    public string? Field { get; init; }

    public static IReadOnlyList<WhatsAppWebhookEnvelope> Split(string rawPayload, bool matchContactOnly = false)
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
                if (Read(change, "field") == "account_update" && Read(entry, "id") is { Length: > 0 } accountId)
                {
                    var accountPayload = JsonSerializer.Serialize(new { @object = "whatsapp_business_account",
                        entry = new[] { new { id = accountId, changes = new[] { change.Clone() } } } });
                    var eventTime = entry.TryGetProperty("time", out var time) ? time.GetRawText() : "";
                    result.Add(new(Hash($"{accountPayload}:{eventTime}"), Hash($"account:{accountId}"), accountPayload, $"account:{accountId}", null, null, []));
                    continue;
                }
                var phoneId = value.TryGetProperty("metadata", out var metadata) ? Read(metadata, "phone_number_id") : null;
                if (string.IsNullOrEmpty(phoneId)) continue;
                var field = Read(change, "field");
                if (field is "smb_message_echoes" or "history" or "smb_app_state_sync")
                {
                    void Add(object body, string? phone, string? id = null, string? userId = null)
                    {
                        var payload = JsonSerializer.Serialize(new { @object = "whatsapp_business_account",
                            entry = new[] { new { id = Read(entry, "id"), changes = new[] { new { field, value = body } } } } });
                        var normalizedPhone = WhatsAppWebhookIdentityReader.NormalizePhone(phone);
                        var aliases = AliasesFor(phoneId, normalizedPhone, userId);
                        result.Add(new(Hash($"{phoneId}:{field}:{id ?? Hash(payload)}"),
                            aliases.FirstOrDefault() ?? Hash($"{phoneId}:sync"), payload, phoneId,
                            normalizedPhone, userId, aliases) { Field = field });
                    }
                    Dictionary<string, object> Body(string name, object item) => new()
                    {
                        ["metadata"] = metadata.Clone(), ["messaging_product"] = "whatsapp", [name] = new[] { item }
                    };
                    if (field == "smb_message_echoes" && value.TryGetProperty("message_echoes", out var echoes))
                        foreach (var item in echoes.EnumerateArray())
                        {
                            var identity = WhatsAppWebhookIdentityReader.ReadEchoRecipient(item);
                            Add(Body("message_echoes", item.Clone()), identity.PhoneNumber, Read(item, "id"), identity.UserId);
                        }
                    if (field == "smb_app_state_sync" && value.TryGetProperty("state_sync", out var contacts))
                        foreach (var item in contacts.EnumerateArray())
                            Add(Body("state_sync", item.Clone()), item.TryGetProperty("contact", out var contact) ? Read(contact, "phone_number") : null);
                    if (field == "history" && value.TryGetProperty("history", out var history))
                        foreach (var chunk in history.EnumerateArray())
                        {
                            if (chunk.TryGetProperty("threads", out var threads) && threads.GetArrayLength() > 0)
                                foreach (var thread in threads.EnumerateArray())
                                {
                                    var copy = chunk.EnumerateObject().ToDictionary(x => x.Name, x => (object)x.Value.Clone());
                                    copy["threads"] = new[] { thread.Clone() };
                                    Add(Body("history", copy), Read(thread, "id"));
                                }
                            else Add(Body("history", chunk.Clone()), null);
                        }
                    if (value.TryGetProperty("errors", out _)) Add(value.Clone(), null);
                    continue;
                }
                foreach (var property in new[] { "messages", "statuses" })
                {
                    if (!value.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        var payloadValue = value.EnumerateObject()
                            .Where(x => x.Name is not ("messages" or "statuses") && (!matchContactOnly || x.Name != "contacts"))
                            .ToDictionary(x => x.Name, x => (object)x.Value.Clone());
                        payloadValue[property] = new[] { item.Clone() };
                        var identity = property == "messages"
                            ? WhatsAppWebhookIdentityReader.Read(value, item, matchContactOnly)
                            : new WhatsAppInboundIdentity(Read(item, "recipient_id"), Read(item, "recipient_user_id"), null, null);
                        if (matchContactOnly && property == "messages" && value.TryGetProperty("contacts", out var contacts)
                            && contacts.ValueKind == JsonValueKind.Array)
                        {
                            payloadValue["contacts"] = contacts.EnumerateArray().Where(contact =>
                                identity.PhoneNumber is not null
                                && WhatsAppWebhookIdentityReader.NormalizePhone(Read(contact, "wa_id")) == identity.PhoneNumber
                                && (identity.UserId is null || Read(contact, "user_id") == identity.UserId)).ToArray();
                        }
                        var payload = JsonSerializer.Serialize(new
                        {
                            @object = "whatsapp_business_account",
                            entry = new[] { new { id = Read(entry, "id"), changes = new[] {
                                new { field = Read(change, "field"), value = payloadValue } } } }
                        });
                        var aliases = AliasesFor(phoneId, identity.PhoneNumber, identity.UserId);
                        var id = Read(item, "id");
                        var key = string.IsNullOrWhiteSpace(id) ? Hash(payload)
                            : Hash($"{phoneId}:{property}:{id}:{Read(item, "status")}:{Read(item, "timestamp")}");
                        result.Add(new(key, aliases.FirstOrDefault() ?? Hash($"{phoneId}:unknown"), payload,
                            phoneId, identity.PhoneNumber, identity.UserId, aliases) { Field = field });
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
