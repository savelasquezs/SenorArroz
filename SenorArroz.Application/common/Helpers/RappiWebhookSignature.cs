using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SenorArroz.Application.Common.Helpers;

public static class RappiWebhookSignature
{
    public static bool IsValid(string? header, string rawPayload, string secret,
        bool allowSandboxTesterPayload = false) =>
        TryValidate(header, rawPayload, secret, out _, allowSandboxTesterPayload);

    /// <summary>The timestamp returned here is the same value authenticated by the HMAC.</summary>
    public static bool TryValidate(string? header, string rawPayload, string secret,
        out string? authenticatedTimestamp, bool allowSandboxTesterPayload = false)
    {
        authenticatedTimestamp = null;
        if (string.IsNullOrWhiteSpace(secret)
            || !TryParse(header, out var timestamp, out var suppliedSignature))
            return false;

        var supplied = suppliedSignature!.ToLowerInvariant();
        var valid = Matches(timestamp!, rawPayload, secret, supplied);
        if (!valid && allowSandboxTesterPayload)
        {
            var sandboxPayload = NormalizeSandboxTesterPayload(rawPayload);
            valid = sandboxPayload != rawPayload
                && Matches(timestamp!, sandboxPayload, secret, supplied);
        }
        if (valid)
            authenticatedTimestamp = timestamp;
        return valid;
    }

    private static bool TryParse(string? header, out string? timestamp, out string? suppliedSignature)
    {
        timestamp = null;
        suppliedSignature = null;
        if (string.IsNullOrWhiteSpace(header))
            return false;
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1)
                return false;
            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Equals("t", StringComparison.OrdinalIgnoreCase))
            {
                if (timestamp is not null)
                    return false;
                timestamp = value;
            }
            else if (key.Equals("sign", StringComparison.OrdinalIgnoreCase))
            {
                if (suppliedSignature is not null)
                    return false;
                suppliedSignature = value;
            }
        }
        return !string.IsNullOrWhiteSpace(timestamp) && !string.IsNullOrWhiteSpace(suppliedSignature);
    }

    private static bool Matches(
        string timestamp,
        string payload,
        string secret,
        string supplied)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}")))
            .ToLowerInvariant();
        return expected.Length == supplied.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(supplied));
    }

    private static string NormalizeSandboxTesterPayload(string rawPayload)
    {
        var normalized = rawPayload;
        if (normalized.Length > 1 && normalized[0] == '"' && normalized[^1] == '"')
            normalized = normalized[1..^1];
        normalized = normalized.Replace("\\\"", "\"", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @":(\s*)(true|false)", ":\"$2\"");
        normalized = Regex.Replace(normalized, @":(\s*)(-?\d+\.?\d*)([,}\]])", ":\"$2\"$3");
        return Regex.Replace(normalized, @":(\s*)null", ":\"null\"");
    }

    public static string? GetTimestamp(string? header) =>
        TryParse(header, out var timestamp, out _) ? timestamp : null;
}
