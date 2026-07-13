namespace SenorArroz.Application.Options;

public class WhatsAppAiPricingOptions
{
    public const string SectionName = "WhatsAppAiPricing";
    public DateTime EffectiveDate { get; set; }
    public Dictionary<string, Dictionary<string, AiModelPrice>> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AiModelPrice? Find(string provider, string model) =>
        Providers.TryGetValue(provider, out var models) && models.TryGetValue(model, out var price) ? price : null;
}

public class AiModelPrice
{
    public decimal InputPerMillionUsd { get; set; }
    public decimal CachedInputPerMillionUsd { get; set; }
    public decimal OutputPerMillionUsd { get; set; }
}
