namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Rangos de enteros cuya representación decimal empieza por un prefijo de dígitos
/// (misma semántica que el filtro de total en el listado de pedidos).
/// </summary>
public static class OrderTotalPrefixRanges
{
    /// <summary>
    /// Genera pares (min, max) inclusivos para <paramref name="digitsOnly"/> no vacío y solo dígitos.
    /// </summary>
    public static IReadOnlyList<(int Min, int Max)> BuildRanges(string digitsOnly)
    {
        if (string.IsNullOrEmpty(digitsOnly) || !digitsOnly.All(char.IsDigit))
            return [];

        var ranges = new List<(int Min, int Max)>();
        for (var k = 0; k <= 12; k++)
        {
            var minStr = digitsOnly + new string('0', k);
            var maxStr = digitsOnly + new string('9', k);
            if (minStr.Length > 10)
                break;
            if (!long.TryParse(minStr, out var minL) || !long.TryParse(maxStr, out var maxL))
                break;
            if (minL > int.MaxValue)
                break;
            var minI = (int)minL;
            var maxI = (int)Math.Min(maxL, int.MaxValue);
            if (minI <= maxI)
                ranges.Add((minI, maxI));
        }

        return ranges;
    }
}
