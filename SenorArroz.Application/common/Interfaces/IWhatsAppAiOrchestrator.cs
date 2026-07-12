using System.Text.Json;
using SenorArroz.Application.Common.Models;
namespace SenorArroz.Application.Common.Interfaces;
public interface IWhatsAppAiOrchestrator { Task<WhatsAppAiProcessingResult> ProcessIncomingMessageAsync(int conversationId, int incomingMessageId, CancellationToken cancellationToken = default); }
public interface IAiChatProvider { string ProviderName { get; } Task<AiChatResponse> GenerateAsync(AiChatRequest request, CancellationToken cancellationToken = default); }
public interface IAiChatProviderResolver { IAiChatProvider? Resolve(string provider); }
public interface IAgentTool { string Name { get; } string Description { get; } JsonElement ParametersSchema { get; } Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken = default); }
public interface IAgentToolExecutor { IReadOnlyList<AiToolDefinition> Definitions { get; } Task<AgentToolExecutionResult> ExecuteAsync(string name, AgentToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken); }
public record WhatsAppAutomaticSendResult(bool Success, bool TransientFailure, string? WhatsAppMessageId, string? Error);
public interface IWhatsAppAutomaticMessageSender { Task<WhatsAppAutomaticSendResult> SendTextAsync(int conversationId, int incomingMessageId, string attemptId, string text, CancellationToken cancellationToken); Task<WhatsAppAutomaticSendResult> SendTransferTextAsync(int conversationId,int incomingMessageId,string attemptId,string text,CancellationToken cancellationToken); }
public interface IWhatsAppAiWorkQueue { bool TryEnqueue(int conversationId, int messageId); }
public interface IWhatsAppAiMessageClaimer { Task<bool> TryClaimAsync(int conversationId, int messageId, CancellationToken cancellationToken); }
