namespace SenorArroz.Application.Features.WhatsApp.DTOs;

public class WhatsAppAiUsageDto
{
    public int TotalInvocations { get; set; }
    public int IncomingMessagesProcessed { get; set; }
    public int ConversationsServed { get; set; }
    public long InputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int UnpricedInvocations { get; set; }
    public double AverageDurationMs { get; set; }
    public long P95DurationMs { get; set; }
    public double ErrorRate { get; set; }
    public double AverageInvocationsPerMessage { get; set; }
    public double AverageToolCallsPerMessage { get; set; }
    public List<WhatsAppAiUsageBreakdownDto> Breakdown { get; set; } = [];
    public List<WhatsAppAiUsageDailyDto> Daily { get; set; } = [];
}
public record WhatsAppAiUsageBreakdownDto(string Provider, string Model, int Invocations, int MessagesProcessed, long InputTokens, long CachedInputTokens, long OutputTokens, decimal EstimatedCostUsd, int UnpricedInvocations, double AverageDurationMs, double ErrorRate);
public record WhatsAppAiUsageDailyDto(DateTime Date, int Invocations, long InputTokens, long CachedInputTokens, long OutputTokens, decimal EstimatedCostUsd, int UnpricedInvocations);
