namespace SenorArroz.Application.Options;

public sealed class MetaConversionsOptions
{
    public const string SectionName = "MetaConversions";

    public string PixelId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v25.0";
    public string EventSourceUrl { get; set; } = "https://senorarroz.com";
    public string? TestEventCode { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PixelId) && !string.IsNullOrWhiteSpace(AccessToken);
}
