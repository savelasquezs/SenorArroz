using System.Text.Json;
using SenorArroz.Domain.Enums;
namespace SenorArroz.Application.Common.Models;
public record AiChatMessage(string Role, string? Content, string? ToolCallId = null, IReadOnlyList<AiToolCall>? ToolCalls = null);
public record AiToolDefinition(string Name, string Description, JsonElement ParametersSchema);
public record AiToolSchemaValidationError(string ToolName, string Location, string Message)
{
    public override string ToString() => $"Herramienta '{ToolName}', {Location}: {Message}";
}
public record AiToolCall(string Id, string Name, JsonElement Arguments, string? ProviderMetadata = null);
public record AiChatRequest(string Model, string ApiKey, IReadOnlyList<AiChatMessage> Messages, IReadOnlyList<AiToolDefinition> Tools, double? Temperature);
public record AiChatUsage(int? InputTokens, int? CachedInputTokens, int? OutputTokens, int? ThinkingTokens);
public record AiChatResponse(string? Text, IReadOnlyList<AiToolCall> ToolCalls, string Model, string? FinishReason, int? InputTokens, int? OutputTokens, bool IsTransientError = false, string? Error = null, int? HttpStatusCode = null, int? CachedInputTokens = null, int? ThinkingTokens = null)
{
    public AiChatUsage Usage => new(InputTokens, CachedInputTokens, OutputTokens, ThinkingTokens);
}
public record AgentToolExecutionContext(int ConversationId,int BranchId,int? IncomingMessageId=null,string? PhoneNumber=null,int? CustomerId=null,int? DraftId=null,string? AttentionMode=null,string? ExecutionId=null,string? TechnicalActor=null);
public sealed class WhatsAppSimpleOrderState { public int Version { get; set; }=1;public List<WhatsAppSimpleOrderItem> Items { get; set; }=[];public List<string> AppliedOperationKeys { get; set; }=[];public int? SelectedAddressId { get; set; }public OrderType? OrderType { get; set; }public DateTime UpdatedAt { get; set; } }
public sealed class WhatsAppSimpleOrderItem { public int ProductId { get; set; }public int Quantity { get; set; }public string? Notes { get; set; } }
public record WhatsAppSimpleOrderSummary(IReadOnlyList<WhatsAppSimpleOrderSummaryItem> Items,int Subtotal,int TotalItems);
public record WhatsAppSimpleOrderSummaryItem(int ProductId,string Name,int Quantity,int UnitPrice,int Subtotal,bool Available);
public record WhatsAppReplyButton(string Id,string Title);
public record AgentToolExecutionResult(bool Success,object? Data,string? Error=null,string Code="ok",string? Message=null,bool RequiresUserInput=false,string? SuggestedQuestion=null,bool TransferredToHuman=false,bool Retryable=false,IReadOnlyList<string>? Warnings=null);
public record WhatsAppAiProcessingResult(bool Processed, bool Ignored, string? IgnoreReason, bool ResponseSent, bool TransferredToHuman, string? Provider, string? Model, int ModelCalls, int ToolsExecuted, string? Error);
