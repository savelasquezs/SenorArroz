namespace SenorArroz.Application.Options;
public class WhatsAppAiContextOptimizationOptions
{
    public const string SectionName = "WhatsAppAiContextOptimization";
    public int OptimizedMaxRecentMessages { get; set; } = 8;
    public bool FallbackToLegacyOnPlannerError { get; set; } = true;
    public int MaxRuntimeContextCharacters { get; set; } = 6000;
}
