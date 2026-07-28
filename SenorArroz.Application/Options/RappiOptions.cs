namespace SenorArroz.Application.Options;

public sealed class RappiOptions
{
    public const string SectionName = "Rappi";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Audience { get; set; } = "https://int-public-api-v2/api";
    public string GrantType { get; set; } = "client_credentials";
    public string AuthUrl { get; set; } = "https://rests-integrations-dev.auth0.com/oauth/token";
    public string ApiBaseUrl { get; set; } = "https://microservices.dev.rappi.com/api/v2/restaurants-integrations-public-api";
    public int TimeoutSeconds { get; set; } = 20;
    public int RecoveryIntervalSeconds { get; set; } = 60;
    public int PiiCleanupIntervalHours { get; set; } = 24;
}
