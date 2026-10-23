using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SenorArroz.Infrastructure.Helpers;

public static class ResendWebhook
{
    public sealed record DeliveryEvent(string EmailId, string Status, DateTime OccurredAt, string[] Recipients, int? OutboxId, string? Error);

    public static bool Verify(string body, string id, string timestamp, string signature, string? secret, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(id) || id.Length > 256
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs((decimal)new DateTimeOffset(now).ToUnixTimeSeconds() - seconds) > 300) return false;
        try
        {
            var key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret);
            if (key.Length < 16) return false;
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
            return signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(part =>
            {
                var fields = part.Split(',', 2);
                if (fields.Length != 2 || fields[0] != "v1") return false;
                try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(fields[1])); }
                catch (FormatException) { return false; }
            });
        }
        catch (FormatException) { return false; }
    }

    public static DeliveryEvent? Read(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var status = root.GetProperty("type").GetString() switch
        {
            "email.delivered" => "delivered", "email.bounced" => "bounced", "email.delivery_delayed" => "delayed",
            "email.failed" => "failed", "email.suppressed" => "suppressed", "email.complained" => "complained", _ => null
        };
        if (status is null) return null;
        var data = root.GetProperty("data");
        var emailId = data.GetProperty("email_id").GetString();
        if (!Guid.TryParse(emailId, out _)) throw new JsonException("Identificador de correo inválido.");
        if (!DateTimeOffset.TryParse(root.GetProperty("created_at").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var occurred))
            throw new JsonException("Fecha de evento inválida.");
        var recipients = data.GetProperty("to").EnumerateArray().Select(x => x.GetString()?.Trim() ?? "").ToArray();
        int? outboxId = data.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("outbox_id", out var tag) && int.TryParse(tag.GetString(), out var value) && value > 0 ? value : null;
        string? error = null;
        if (data.TryGetProperty("bounce", out var bounce) && bounce.ValueKind == JsonValueKind.Object && bounce.TryGetProperty("message", out var message)) error = message.GetString();
        else if (data.TryGetProperty("failed", out var failed) && failed.ValueKind == JsonValueKind.Object && failed.TryGetProperty("reason", out var reason)) error = reason.GetString();
        return new(emailId!, status, occurred.UtcDateTime, recipients, outboxId, error is { Length: > 2000 } ? error[..2000] : error);
    }

    public static bool CanAdvance(string? current, DateTime? at, DeliveryEvent next) =>
        (at is null || next.OccurredAt >= at) && Rank(next.Status) >= Rank(current);

    private static int Rank(string? status) => status switch { "delayed" => 1, "delivered" => 2, "failed" or "bounced" or "suppressed" => 3, "complained" => 4, _ => 0 };
}
