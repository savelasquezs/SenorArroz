namespace SenorArroz.Application.Features.WhatsApp.DTOs;

public sealed record WhatsAppOrderConversationDto(
    string Kind, int? ConversationId = null, string? Phone = null, int? BranchId = null);
