using System.Text.Json;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.WhatsApp.DTOs;

public sealed class WhatsAppDraftWriteDto
{
    public int SessionId { get; set; }
    public int ExpectedVersion { get; set; }
    public long AttentionVersion { get; set; }
    public CreateOrderDto Order { get; set; } = new();
    public JsonElement? Editor { get; set; }
    public string CommercialStatus { get; set; } = "in_progress";
    public string? ReviewedFingerprint { get; set; }
    public string? Note { get; set; }
}

public sealed record WhatsAppInternalNote(int UserId, string Text, DateTime CreatedAt);
public sealed record WhatsAppProductChoiceGroupDto(int Id, string Name, bool IsRice, string? Shortcut);
public sealed record WhatsAppCommercialDraftDto(int SessionId, Guid AttemptId, int ConversationId,
    int Version, string Status, string CommercialStatus, CreateOrderDto Order, JsonElement? Editor,
    int? OrderId, string? CheckoutId, string? ConfirmationStatus, string? ConfirmationError,
    IReadOnlyList<WhatsAppInternalNote> InternalNotes);
public sealed record WhatsAppDraftQuoteDto(string Fingerprint, int Total, IReadOnlyList<string> Changes,
    IReadOnlyList<string> Errors);
