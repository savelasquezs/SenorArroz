using System.Text.Json;

namespace SenorArroz.Application.Common.Services;

public sealed record WhatsAppInboundIdentity(
    string? PhoneNumber,
    string? UserId,
    string? Username,
    string? ContactName);

public static class WhatsAppWebhookIdentityReader
{
    public static WhatsAppInboundIdentity ReadEchoRecipient(JsonElement message) =>
        new(NormalizePhone(GetString(message, "to")),
            WhatsAppIdentityNormalizer.NormalizeUserId(GetString(message, "to_user_id")), null, null);

    public static WhatsAppInboundIdentity Read(JsonElement value, JsonElement message, bool matchContactOnly = false)
    {
        var phoneNumber = NormalizePhone(GetString(message, "from"));
        var userId = WhatsAppIdentityNormalizer.NormalizeUserId(GetString(message, "from_user_id"));
        JsonElement? matchedContact = null;
        JsonElement? firstContact = null;
        var contactCount = 0;

        if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
        {
            foreach (var contactElement in contacts.EnumerateArray())
            {
                contactCount++;
                firstContact ??= contactElement;
                var contactUserId = WhatsAppIdentityNormalizer.NormalizeUserId(GetString(contactElement, "user_id"));
                var contactPhone = NormalizePhone(GetString(contactElement, "wa_id"));
                if ((userId is not null && string.Equals(userId, contactUserId, StringComparison.Ordinal))
                    || (phoneNumber is not null && string.Equals(phoneNumber, contactPhone, StringComparison.Ordinal)))
                {
                    matchedContact = contactElement;
                    break;
                }
            }
        }

        var selectedContact = matchedContact ?? (!matchContactOnly && contactCount == 1 ? firstContact : null);
        string? contactName = null;
        string? username = null;
        if (selectedContact is { } contact)
        {
            phoneNumber ??= NormalizePhone(GetString(contact, "wa_id"));
            userId ??= WhatsAppIdentityNormalizer.NormalizeUserId(GetString(contact, "user_id"));
            if (contact.TryGetProperty("profile", out var profile))
            {
                contactName = NullIfBlank(GetString(profile, "name"));
                username = WhatsAppIdentityNormalizer.NormalizeUsername(GetString(profile, "username"));
            }
        }

        return new(phoneNumber, userId, username, contactName);
    }

    public static string? NormalizePhone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return NullIfBlank(digits);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
