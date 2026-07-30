namespace SenorArroz.Application.Options;

public sealed class RappiOptions
{
    public const string SectionName = "Rappi";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthUrl { get; set; } = "https://api.dev.rappi.com/restaurants/auth/v1/token/login/integrations";
    public string ApiBaseUrl { get; set; } = "https://microservices.dev.rappi.com/api/v2/restaurants-integrations-public-api";
    public int TimeoutSeconds { get; set; } = 20;
    public int RecoveryIntervalSeconds { get; set; } = 60;
    public int PiiCleanupIntervalHours { get; set; } = 24;
}
