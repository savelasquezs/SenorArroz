using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SenorArroz.Application.Common.Printing;

/// <summary>Abreviaturas de nombre para cocina (pantalla y comanda). Reglas: docs del front, sección Cocina.</summary>
public static class KitchenProductNameFormatter
{
    private static readonly Regex SAlaFrancesa = new(
        @"(?:^|\s)(?:a|á)\s+la\s+fr(?:ancesa|ancésa|ansesa)(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ChichInWord = new(
        @"chicharr(ó|o)n|chicharron",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DigitsGr = new(
        @"\b(\d+)\s*gr\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex XDigit = new(
        @"\b([xX])(\d+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Format(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return string.Empty;

        var trimmed = productName.Trim();
        var working = SAlaFrancesa.Replace(trimmed, " ");
        working = Regex.Replace(working, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(working))
            return trimmed;

        var parts = working.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return trimmed;

        var hasSuper = parts.Any(p => IsSuper(p));
        var hasFamiliar = parts.Any(p => IsFamiliar(p));
        var merged = MergeRopaVieja(parts);
        var result = new List<string>(merged.Length);

        foreach (var w in merged)
        {
            if (IsOmitted(w)) continue;
            if (hasSuper && hasFamiliar && IsFamiliar(w)) continue;
            if (IsSuper(w))
            {
                result.Add("super");
                continue;
            }
            result.Add(ChichInWord.Replace(w, "chich"));
        }

        var s = string.Join(' ', result).Trim();
        if (string.IsNullOrWhiteSpace(s))
            return trimmed;
        s = PutSuperFirst(s);
        s = PostFormat(s);
        return s;
    }

    private static string PutSuperFirst(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return line;
        if (!parts.Any(t => t.Equals("super", StringComparison.Ordinal)))
            return string.Join(' ', parts);
        return string.Join(' ', new[] { "super" }
            .Concat(parts.Where(t => !t.Equals("super", StringComparison.Ordinal))));
    }

    private static string PostFormat(string s)
    {
        s = DigitsGr.Replace(s, "$1g");
        s = XDigit.Replace(s, m => "x " + m.Groups[2].Value);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static string[] MergeRopaVieja(string[] parts)
    {
        var list = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i + 1 < parts.Length
                && string.Equals(RemoveDiacritics(parts[i]!.ToLowerInvariant()), "ropa", StringComparison.Ordinal)
                && string.Equals(RemoveDiacritics(parts[i + 1]!.ToLowerInvariant()), "vieja", StringComparison.Ordinal))
            {
                list.Add("ropa");
                i++;
            }
            else
                list.Add(parts[i]!);
        }
        return list.ToArray();
    }

    private static string LowerStripped(string w) => RemoveDiacritics(w.ToLowerInvariant());

    private static bool IsOmitted(string w)
    {
        return LowerStripped(w) is "arroz" or "con" or "de" or "unidades";
    }

    private static bool IsSuper(string w) => LowerStripped(w) == "super";

    private static bool IsFamiliar(string w) => LowerStripped(w) == "familiar";

    private static string RemoveDiacritics(string s)
    {
        var n = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var c in n)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat is not UnicodeCategory.NonSpacingMark
                and not UnicodeCategory.SpacingCombiningMark
                and not UnicodeCategory.EnclosingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
