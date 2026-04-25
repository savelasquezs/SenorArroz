namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Patrones para búsquedas con <c>EF.Functions.ILike</c> en PostgreSQL.
/// </summary>
public static class SqlSearchPattern
{
    /// <summary>Escapa <c>%</c>, <c>_</c> y <c>\</c> para usar dentro de un patrón LIKE/ILIKE.</summary>
    public static string EscapeForLike(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    /// <summary>Patrón ILIKE case-insensitive “contiene” el texto bruto.</summary>
    public static string ILikeContains(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "%";
        return "%" + EscapeForLike(raw) + "%";
    }
}
