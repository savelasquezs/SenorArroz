namespace SenorArroz.Domain.Entities;

public class WhatsAppAiInvocation
{
    public long Id { get; set; }
    public int BranchId { get; set; }
    public int ConversationId { get; set; }
    public int IncomingMessageId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InvocationIndex { get; set; }
    public int AttemptIndex { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int? InputTokens { get; set; }
    public int? CachedInputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? ThinkingTokens { get; set; }
    public int? BillableOutputTokens { get; set; }
    public int ToolCallCount { get; set; }
    public string? FinishReason { get; set; }
    public bool Success { get; set; }
    public bool IsTransientError { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? InputPricePerMillionUsd { get; set; }
    public decimal? CachedInputPricePerMillionUsd { get; set; }
    public decimal? OutputPricePerMillionUsd { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public DateTime? PricingEffectiveDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ContextStrategy { get; set; } = "legacy";
    public int? ContextMessageCount { get; set; }
    public int? ToolDefinitionCount { get; set; }
    public int? SystemPromptCharacters { get; set; }
    public int? RuntimeContextCharacters { get; set; }
    public int? HistoryCharacters { get; set; }
    public int? ToolDefinitionsCharacters { get; set; }
    public bool ContextPlannerFallback { get; set; }
    public string? ContextPlannerFallbackReason { get; set; }

    public Branch Branch { get; set; } = null!;
    public WhatsAppConversation Conversation { get; set; } = null!;
    public WhatsAppMessage IncomingMessage { get; set; } = null!;
}
