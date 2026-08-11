using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Infrastructure;

public static class DeliveryAppVersionHeaders
{
    public const string Client = "X-Senor-Arroz-Client";
    public const string WebClient = "web";
    public const string Version = "X-Delivery-App-Version";
    public const string Build = "X-Delivery-App-Build";
    public const string Package = "X-Delivery-App-Package";

    public static bool IsWebClient(HttpRequest request) =>
        string.Equals(
            request.Headers[Client].FirstOrDefault(),
            WebClient,
            StringComparison.OrdinalIgnoreCase);

    public static DeliveryAppClientVersion Read(HttpRequest request, bool allowQuery = false)
    {
        var versionName = ReadValue(request, Version, "version", allowQuery);
        var buildValue = ReadValue(request, Build, "build", allowQuery);
        var packageName = ReadValue(request, Package, "packageName", allowQuery);

        return new DeliveryAppClientVersion(
            versionName,
            int.TryParse(buildValue, out var buildNumber) ? buildNumber : null,
            packageName);
    }

    private static string? ReadValue(
        HttpRequest request,
        string headerName,
        string queryName,
        bool allowQuery)
    {
        var headerValue = request.Headers[headerName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerValue))
            return headerValue;

        return allowQuery ? request.Query[queryName].FirstOrDefault() : null;
    }
}
