namespace SenorArroz.Application.Common.Printing;

/// <summary>Abreviaturas para línea de cocina: omite "arroz" / "con", "chicharron..." → "chich...".</summary>
public static class KitchenProductNameFormatter
{
    public static string Format(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return string.Empty;

        var parts = productName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(parts.Length);
        foreach (var w in parts)
        {
            var lw = w.ToLowerInvariant();
            if (lw is "arroz" or "con")
                continue;

            const string ch = "chicharron";
            if (lw.StartsWith(ch, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(lw.Length == ch.Length ? "chich" : "chich" + w.Substring(ch.Length));
                continue;
            }

            result.Add(w);
        }

        var s = string.Join(' ', result).Trim();
        return string.IsNullOrWhiteSpace(s) ? productName.Trim() : s;
    }
}
