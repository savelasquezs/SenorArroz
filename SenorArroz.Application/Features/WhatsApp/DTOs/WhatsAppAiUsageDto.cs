namespace SenorArroz.Application.Features.WhatsApp.DTOs;

public class WhatsAppAiUsageDto
{
    public int TotalInvocations { get; set; }
    public int IncomingMessagesProcessed { get; set; }
    public int ConversationsServed { get; set; }
    public long InputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long ThinkingTokens { get; set; }
    public long BillableOutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int UnpricedInvocations { get; set; }
    public double AverageDurationMs { get; set; }
    public long P95DurationMs { get; set; }
    public double ErrorRate { get; set; }
    public double AverageInvocationsPerMessage { get; set; }
    public double AverageToolCallsPerMessage { get; set; }
    public double AverageContextMessages { get; set; }
    public double AverageToolDefinitions { get; set; }
    public double ContextPlannerFallbackRate { get; set; }
    public List<WhatsAppAiUsageBreakdownDto> Breakdown { get; set; } = [];
    public List<WhatsAppAiUsageDailyDto> Daily { get; set; } = [];
}
public record WhatsAppAiUsageBreakdownDto(string Provider, string Model, string ContextStrategy, int Invocations, int MessagesProcessed, long InputTokens, long CachedInputTokens, long OutputTokens, long ThinkingTokens, long BillableOutputTokens, decimal EstimatedCostUsd, int UnpricedInvocations, double AverageDurationMs, double ErrorRate, double AverageContextMessages, double AverageToolDefinitions, double FallbackRate);
public record WhatsAppAiUsageDailyDto(DateTime Date, int Invocations, long InputTokens, long CachedInputTokens, long OutputTokens, long ThinkingTokens, long BillableOutputTokens, decimal EstimatedCostUsd, int UnpricedInvocations);
