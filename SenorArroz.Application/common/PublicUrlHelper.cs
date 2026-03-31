namespace SenorArroz.Application.Common;

public static class PublicUrlHelper
{
    public static string? ToAbsolutePublicUrl(string? baseUrl, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(relativePath))
            return null;
        var b = baseUrl.Trim().TrimEnd('/');
        var p = relativePath.Trim();
        if (!p.StartsWith('/'))
            p = '/' + p;
        return b + p;
    }
}
