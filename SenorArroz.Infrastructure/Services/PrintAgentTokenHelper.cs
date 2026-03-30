using System.Security.Cryptography;
using System.Text;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Services;

public static class PrintAgentTokenHelper
{
    /// <summary>SHA-256 en hex minúsculas de <c>salt + plainToken</c> (mismo algoritmo al rotar token en admin).</summary>
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
}
