namespace SenorArroz.Application.Options;

public class WhatsAppCloudOptions
{
    public const string SectionName = "WhatsAppCloud";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string GraphApiVersion { get; set; } = "v20.0";
    public string? AccessToken { get; set; }
    public string? BusinessAccountId { get; set; }
    public string? PhoneNumberId { get; set; }
}
