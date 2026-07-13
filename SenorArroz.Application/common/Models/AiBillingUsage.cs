namespace SenorArroz.Application.Common.Models;

public record AiBillingUsage(int UncachedInputTokens, int CachedInputTokens, int BillableOutputTokens, int VisibleOutputTokens, int ThinkingTokens)
{
    public static AiBillingUsage From(string provider, AiChatResponse response)
    {
        var input = Math.Max(0, response.InputTokens ?? 0);
        var cached = Math.Clamp(response.CachedInputTokens ?? 0, 0, input);
        var visible = Math.Max(0, response.OutputTokens ?? 0);
        var thinking = Math.Max(0, response.ThinkingTokens ?? 0);
        var billableOutput = provider.Equals("gemini", StringComparison.OrdinalIgnoreCase)
            ? visible + thinking
            : visible;
        return new(input - cached, cached, billableOutput, visible, thinking);
    }
}
