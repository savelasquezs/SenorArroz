using System.Security.Cryptography;
using System.Text;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppWebhookSignature
{
    public static bool IsValid(string? header, string payload, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(appSecret)
            || !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;
        byte[] received;
        try { received = Convert.FromHexString(header[7..]); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(payload));
        return received.Length == expected.Length && CryptographicOperations.FixedTimeEquals(received, expected);
    }
}
