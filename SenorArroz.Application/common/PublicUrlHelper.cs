namespace SenorArroz.Application.Common;

public static class PublicUrlHelper
{
    /// <summary>
    /// Si <paramref name="pathOrUrl"/> ya es http(s) absoluta, se devuelve tal cual (ignora <paramref name="baseUrl"/>).
    /// Si es ruta relativa al host API, se antepone <paramref name="baseUrl"/>; sin baseUrl válida, null.
    /// </summary>
    public static string? ToAbsolutePublicUrl(string? baseUrl, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return null;

        var p = pathOrUrl.Trim();
        if (IsAbsoluteHttp(p))
            return p;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var b = baseUrl.Trim().TrimEnd('/');
        if (!p.StartsWith('/'))
            p = '/' + p;
        return b + p;
    }

    private static bool IsAbsoluteHttp(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
