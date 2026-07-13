using System.Text.Json;
namespace SenorArroz.Application.Common.Models;
public record AiChatMessage(string Role, string? Content, string? ToolCallId = null, IReadOnlyList<AiToolCall>? ToolCalls = null);
public record AiToolDefinition(string Name, string Description, JsonElement ParametersSchema);
public record AiToolCall(string Id, string Name, JsonElement Arguments, string? ProviderMetadata = null);
public record AiChatRequest(string Model, string ApiKey, IReadOnlyList<AiChatMessage> Messages, IReadOnlyList<AiToolDefinition> Tools, double? Temperature);
public record AiChatUsage(int? InputTokens, int? CachedInputTokens, int? OutputTokens, int? ThinkingTokens);
public record AiChatResponse(string? Text, IReadOnlyList<AiToolCall> ToolCalls, string Model, string? FinishReason, int? InputTokens, int? OutputTokens, bool IsTransientError = false, string? Error = null, int? HttpStatusCode = null, int? CachedInputTokens = null, int? ThinkingTokens = null)
{
    public AiChatUsage Usage => new(InputTokens, CachedInputTokens, OutputTokens, ThinkingTokens);
}
public record AgentToolExecutionContext(int ConversationId,int BranchId,int? IncomingMessageId=null,string? PhoneNumber=null,int? CustomerId=null,int? DraftId=null,string? AttentionMode=null,string? ExecutionId=null,string? TechnicalActor=null);
public record WhatsAppProductMatchCandidate(int ProductId,string Name,int Price,bool Available,int? ServesPeopleMin,int? ServesPeopleMax,string MatchType,double Score);
public record WhatsAppProductMatchResult(string NormalizedQuery,int? ServesPeople,bool ExactMatch,bool ApproximateMatch,bool NeedsClarification,IReadOnlyList<WhatsAppProductMatchCandidate> Products);
public record WhatsAppReplyButton(string Id,string Title);
public record AgentToolExecutionResult(bool Success,object? Data,string? Error=null,string Code="ok",string? Message=null,bool RequiresUserInput=false,string? SuggestedQuestion=null,bool TransferredToHuman=false,bool Retryable=false,IReadOnlyList<string>? Warnings=null);
public record WhatsAppAiProcessingResult(bool Processed, bool Ignored, string? IgnoreReason, bool ResponseSent, bool TransferredToHuman, string? Provider, string? Model, int ModelCalls, int ToolsExecuted, string? Error);
