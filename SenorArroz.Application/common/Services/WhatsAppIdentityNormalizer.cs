using System.Text.RegularExpressions;

namespace SenorArroz.Application.Common.Services;

public static partial class WhatsAppIdentityNormalizer
{
    public static string? NormalizeUsername(string? value)
    {
        var username = value?.Trim().ToLowerInvariant().TrimStart('@');
        return string.IsNullOrWhiteSpace(username) ? null : $"@{username}";
    }

    public static string? NormalizeUserId(string? value)
    {
        var userId = value?.Trim();
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }

    public static bool IsValidUsername(string? value)
    {
        var username = NormalizeUsername(value);
        if (username is null)
            return false;

        var handle = username[1..];
        return handle.Length is >= 3 and <= 35
            && handle.Any(char.IsLetter)
            && UsernameCharactersRegex().IsMatch(handle)
            && !handle.StartsWith('.')
            && !handle.EndsWith('.')
            && !handle.Contains("..", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z0-9._]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameCharactersRegex();
}
