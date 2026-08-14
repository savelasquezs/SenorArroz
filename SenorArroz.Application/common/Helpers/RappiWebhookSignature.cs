using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SenorArroz.Application.Common.Helpers;

public static class RappiWebhookSignature
{
    public static bool IsValid(string? header, string rawPayload, string secret)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(secret))
            return false;

        string? timestamp = null;
        string? suppliedSignature = null;
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1)
                continue;
            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Equals("t", StringComparison.OrdinalIgnoreCase))
                timestamp = value;
            else if (key.Equals("sign", StringComparison.OrdinalIgnoreCase))
                suppliedSignature = value;
        }

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(suppliedSignature))
            return false;

        var supplied = suppliedSignature.ToLowerInvariant();
        if (Matches(timestamp, rawPayload, secret, supplied))
            return true;

        var sandboxPayload = NormalizeSandboxTesterPayload(rawPayload);
        return sandboxPayload != rawPayload
            && Matches(timestamp, sandboxPayload, secret, supplied);
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

    public static string? GetTimestamp(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;
        return header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(x => x.Length == 2 && x[0].Equals("t", StringComparison.OrdinalIgnoreCase))
            .Select(x => x[1])
            .FirstOrDefault();
    }
}
