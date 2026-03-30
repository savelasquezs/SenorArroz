using System.Security.Cryptography;
using System.Text;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Printing;

public static class PrintAgentTokenCrypto
{
    /// <summary>SHA-256 hex minúsculas de <c>salt + plainToken</c>.</summary>
    public static string ComputeHash(string salt, string plainToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + plainToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsValid(string? plainToken, BranchPrintSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentTokenHash)) return false;
        if (string.IsNullOrEmpty(plainToken)) return false;
        try
        {
            var computed = ComputeHash(settings.AgentTokenSalt, plainToken);
            var a = Convert.FromHexString(computed);
            var b = Convert.FromHexString(settings.AgentTokenHash);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Salt hexadecimal (para persistir en <c>agent_token_salt</c>).</summary>
    public static string NewSalt(int byteLength = 16)
    {
        var buf = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    /// <summary>Token en claro URL-safe (copiar al appsettings del agente).</summary>
    public static string NewPlainToken(int byteLength = 32)
    {
        var buf = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
