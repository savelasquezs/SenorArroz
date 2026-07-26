using System.Text.Json;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Entities;
namespace SenorArroz.Application.Common.Interfaces;
public interface IWhatsAppAiOrchestrator { Task<WhatsAppAiProcessingResult> ProcessIncomingMessageAsync(int conversationId, int incomingMessageId, CancellationToken cancellationToken = default); }
public interface IAiChatProvider { string ProviderName { get; } Task<AiChatResponse> GenerateAsync(AiChatRequest request, CancellationToken cancellationToken = default); }
public interface IAiChatProviderResolver { IAiChatProvider? Resolve(string provider); }
public interface IAgentTool { string Name { get; } string Description { get; } JsonElement ParametersSchema { get; } string Category=>"general";bool ModifiesData=>false;string RiskLevel=>"low";bool RequiresAiMode=>true;IReadOnlyList<string> Prerequisites=>[];Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken = default); }
public interface IAgentToolExecutor { IReadOnlyList<AiToolDefinition> Definitions { get; } Task<AgentToolExecutionResult> ExecuteAsync(string name, AgentToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken); }
public interface IAgentToolCatalog { IReadOnlyList<AiToolDefinition> All { get; } IReadOnlyList<AiToolDefinition> GetByNames(IEnumerable<string> names); bool ModifiesData(string name); }
public interface IAiToolSchemaValidator { AiToolSchemaValidationError? Validate(IReadOnlyList<AiToolDefinition> tools); void ValidateOrThrow(IReadOnlyList<AiToolDefinition> tools); }
public interface IWhatsAppSimpleOrderStateService { Task<WhatsAppSimpleOrderState> LoadAsync(int conversationId,CancellationToken cancellationToken=default);Task SaveAsync(int conversationId,WhatsAppSimpleOrderState state,CancellationToken cancellationToken=default);Task ClearAsync(int conversationId,CancellationToken cancellationToken=default);Task<WhatsAppSimpleOrderSummary> BuildSummaryAsync(int branchId,WhatsAppSimpleOrderState state,CancellationToken cancellationToken=default); }
public record WhatsAppAutomaticSendResult(bool Success, bool TransientFailure, string? WhatsAppMessageId, string? Error, bool InProgress = false);
public interface IWhatsAppAutomaticMessageSender { Task<WhatsAppAutomaticSendResult> SendTextAsync(int conversationId, int incomingMessageId, string attemptId, string text, CancellationToken cancellationToken); Task<WhatsAppAutomaticSendResult> SendTransferTextAsync(int conversationId,int incomingMessageId,string attemptId,string text,CancellationToken cancellationToken);Task<WhatsAppAutomaticSendResult> SendAwayTextAsync(int conversationId,string dispatchKey,string text,CancellationToken cancellationToken);Task<WhatsAppAutomaticSendResult> SendAgentContentAsync(int conversationId,string dispatchKey,string text,string? imageUrl,CancellationToken cancellationToken);Task<WhatsAppAutomaticSendResult> SendAgentReplyButtonsAsync(int conversationId,string dispatchKey,string text,IReadOnlyList<WhatsAppReplyButton> buttons,CancellationToken cancellationToken); }
public interface IWhatsAppAiWorkQueue { bool TryEnqueue(int conversationId, int messageId); }
public interface IWhatsAppAiMessageClaimer
{
    Task<bool> TryClaimAsync(int conversationId, int messageId, CancellationToken cancellationToken);
    Task<bool> TryCompleteAsync(
        int conversationId,
        int messageId,
        DateTime processedAt,
        CancellationToken cancellationToken);
}
