using System.Globalization;
using System.Text;

namespace SenorArroz.Application.Common.Services;

public static class ComplexAccessKeywordEvaluator
{
    public static IReadOnlyList<string> ParseKeywords(string commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return Array.Empty<string>();

        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(s => s.Length > 0)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Devuelve si hay coincidencia y el término original (primer keyword que matchea).
    /// </summary>
    public static (bool Matches, string? FirstMatchedKeyword) Evaluate(string? addressText, IReadOnlyList<string> normalizedKeywords)
    {
        if (normalizedKeywords.Count == 0 || string.IsNullOrWhiteSpace(addressText))
            return (false, null);

        var hay = Normalize(addressText);
        foreach (var kw in normalizedKeywords)
        {
            if (hay.Contains(kw, StringComparison.Ordinal))
                return (true, kw);
        }

        return (false, null);
    }

    private static string Normalize(string input)
    {
        var formD = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(capacity: formD.Length);
        foreach (var c in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
