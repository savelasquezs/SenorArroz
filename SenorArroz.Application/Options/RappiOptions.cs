namespace SenorArroz.Application.Options;

public sealed class RappiOptions
{
    public const string SectionName = "Rappi";

    public string Environment { get; set; } = "sandbox";
    public bool ProductionEnabled { get; set; }
    public bool ProductionReadyForPickupApproved { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthUrl { get; set; } = "https://api.dev.rappi.com/restaurants/auth/v1/token/login/integrations";
    public string ApiBaseUrl { get; set; } = "https://api.dev.rappi.com/api/v2/restaurants-integrations-public-api";
    public int TimeoutSeconds { get; set; } = 20;
    public int RecoveryIntervalSeconds { get; set; } = 60;
    public int PiiCleanupIntervalHours { get; set; } = 24;

    public bool HasValidEnvironmentConfiguration()
    {
        if (Environment is not ("sandbox" or "production")) return false;
        if (!Uri.TryCreate(AuthUrl, UriKind.Absolute, out var auth)
            || !Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var api)
            || auth.Scheme != "https" || api.Scheme != "https"
            || auth.UserInfo.Length > 0 || api.UserInfo.Length > 0
            || auth.Query.Length > 0 || api.Query.Length > 0
            || auth.Fragment.Length > 0 || api.Fragment.Length > 0
            || auth.AbsolutePath.TrimEnd('/') != "/restaurants/auth/v1/token/login/integrations"
            || api.AbsolutePath.TrimEnd('/') != "/api/v2/restaurants-integrations-public-api") return false;
        if (Environment == "sandbox")
            return auth.Host == "api.dev.rappi.com" && api.Host is "api.dev.rappi.com" or "microservices.dev.rappi.com";
        return ProductionEnabled && auth.Host == "api.rappi.com.co"
            && api.Host is "services.rappi.com" or "api.rappi.com.co";
    }
}
