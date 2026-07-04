namespace SenorArroz.Application.Options;

public class WhatsAppCloudOptions
{
    public const string SectionName = "WhatsAppCloud";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string GraphApiVersion { get; set; } = "v20.0";
}
