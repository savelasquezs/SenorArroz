using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Services;

public partial class WhatsAppWebhookProcessor
{
    private const string EditedAtKey = "_pos_edited_at";
    private static string? StructuredMessageLabel(string? type) => type switch
    {
        "location" => "Ubicación compartida",
        "contacts" => "Contacto compartido",
        _ => null
    };

    private static void ReadMessageContent(WhatsAppMessageDto dto, string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload)) return;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty(EditedAtKey, out var edit) && edit.ValueKind == JsonValueKind.String && edit.TryGetDateTime(out var editedAt))
                dto.EditedAt = AsUtc(editedAt);
            if (root.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object)
                dto.ReplyToWhatsAppMessageId = TryGetString(context, "id") ?? TryGetString(context, "message_id");
            if (TryGetString(root, "type") == "location" && root.TryGetProperty("location", out var location)
                && location.ValueKind == JsonValueKind.Object
                && location.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number && lat.TryGetDouble(out var latitude)
                && location.TryGetProperty("longitude", out var lng) && lng.ValueKind == JsonValueKind.Number && lng.TryGetDouble(out var longitude)
                && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
            {
                dto.Type = "location";
                dto.Location = new(latitude, longitude, TryGetString(location, "name"), TryGetString(location, "address"));
            }
            if (TryGetString(root, "type") == "contacts" && root.TryGetProperty("contacts", out var contacts)
                && contacts.ValueKind == JsonValueKind.Array)
            {
                dto.Type = "contacts";
                dto.Contacts = contacts.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Take(20).Select(ReadContactCard).ToArray();
            }
        }
        catch (JsonException) { }
    }

    private async Task ApplyProviderEditAsync(WhatsAppConversation? conversation, JsonElement item, DateTime timestamp,
        bool outbound, List<WhatsAppMessage> updates, CancellationToken ct)
    {
        if (conversation is null || !item.TryGetProperty("edit", out var edit) || edit.ValueKind != JsonValueKind.Object
            || !edit.TryGetProperty("message", out var content) || content.ValueKind != JsonValueKind.Object)
            return;
        var originalId = TryGetString(edit, "original_message_id");
        if (string.IsNullOrWhiteSpace(originalId)) return;

        var original = _db.WhatsAppMessages.Local.FirstOrDefault(x => x.Conversation == conversation && x.WhatsAppMessageId == originalId)
            ?? await _db.WhatsAppMessages.SingleOrDefaultAsync(x => x.ConversationId == conversation.Id
                && x.WhatsAppMessageId == originalId, ct);
        if (original is null || original.HiddenAt.HasValue || timestamp < original.Timestamp
            || original.Direction != (outbound ? WhatsAppMessageDirection.Outbound : WhatsAppMessageDirection.Inbound))
            return;

        var editedText = TryGetTextBody(content);
        var type = TryGetString(content, "type");
        if (editedText is null && type is not null && content.TryGetProperty(type, out var attachment)
            && attachment.ValueKind == JsonValueKind.Object)
            editedText = TryGetString(attachment, "caption");
        if (editedText is null) return;

        JsonObject payload;
        try { payload = JsonNode.Parse(original.RawPayload ?? "{}") as JsonObject ?? new JsonObject(); }
        catch (JsonException) { payload = new JsonObject(); }
        if (payload[EditedAtKey] is JsonValue last && last.TryGetValue<DateTime>(out var previouslyEdited)
            && (previouslyEdited > timestamp || previouslyEdited == timestamp && original.TextBody == editedText))
            return;

        original.TextBody = editedText[..Math.Min(editedText.Length, 4096)];
        payload[EditedAtKey] = AsUtc(timestamp);
        original.RawPayload = payload.ToJsonString();
        if (conversation.LastMessageAt == original.Timestamp && !await _db.WhatsAppMessages.AnyAsync(
            x => x.ConversationId == conversation.Id && x.Timestamp == original.Timestamp && x.Id > original.Id, ct))
            conversation.LastMessagePreview = PreviewForMedia(original.Type, original.TextBody);
        updates.Add(original);
    }

    private static WhatsAppMessageContactDto ReadContactCard(JsonElement card)
    {
        var name = card.TryGetProperty("name", out var details) && details.ValueKind == JsonValueKind.Object
            ? TryGetString(details, "formatted_name") : null;
        var phones = card.TryGetProperty("phones", out var entries) && entries.ValueKind == JsonValueKind.Array
            ? entries.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => TryGetString(x, "phone") ?? TryGetString(x, "wa_id")).OfType<string>()
                .Where(phone => !string.IsNullOrWhiteSpace(phone)).Distinct().Take(10).ToArray()
            : [];
        return new(name ?? "Contacto", phones);
    }

    private async Task ApplyProviderReactionAsync(WhatsAppConversation? conversation, JsonElement item, DateTime timestamp,
        bool outbound, List<WhatsAppMessage> updates, CancellationToken ct)
    {
        if (conversation is null || !item.TryGetProperty("reaction", out var reaction) || reaction.ValueKind != JsonValueKind.Object)
            return;
        var targetId = TryGetString(reaction, "message_id");
        if (string.IsNullOrWhiteSpace(targetId) || !reaction.TryGetProperty("emoji", out var value) || value.ValueKind != JsonValueKind.String)
            return;
        var emoji = value.GetString();
        if (emoji is null || emoji.Length > 32) return;
        var target = _db.WhatsAppMessages.Local.FirstOrDefault(x => x.Conversation == conversation && x.WhatsAppMessageId == targetId)
            ?? await _db.WhatsAppMessages.SingleOrDefaultAsync(x => x.ConversationId == conversation.Id
                && x.WhatsAppMessageId == targetId, ct);
        if (target is null || target.HiddenAt.HasValue) return;
        if (outbound)
        {
            if (target.BusinessReactionAt > timestamp) return;
            target.BusinessReaction = emoji.Length == 0 ? null : emoji;
            target.BusinessReactionAt = timestamp;
        }
        else
        {
            if (target.CustomerReactionAt > timestamp) return;
            target.CustomerReaction = emoji.Length == 0 ? null : emoji;
            target.CustomerReactionAt = timestamp;
        }
        updates.Add(target);
    }
}
