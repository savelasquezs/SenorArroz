using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Models;
public record WhatsAppAiContextPlannerInput(WhatsAppConversation Conversation, WhatsAppMessage IncomingMessage, Branch Branch, Customer? Customer, WhatsAppOrderDraft? ActiveDraft, string Strategy, int MaxContextMessages, IReadOnlyList<AiChatMessage> History, IReadOnlyList<AiToolDefinition> AllTools, string SystemPrompt);
public record WhatsAppAiContextPlan(string Strategy, IReadOnlyList<AiChatMessage> Messages, string? StructuredState, IReadOnlyList<string> AllowedToolNames, int HistoryMessageCount, int ToolDefinitionCount, int SystemPromptCharacters, int RuntimeContextCharacters, int HistoryCharacters, int ToolDefinitionsCharacters, bool FallbackToLegacyTools, string? FallbackReason);
