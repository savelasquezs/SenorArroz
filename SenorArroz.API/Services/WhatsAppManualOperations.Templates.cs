using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Services;

public sealed partial class WhatsAppManualOperations
{
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppCloudTemplate>>>> ConversationTemplates(int id, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var channel = await ResolveConversationChannelAsync(conversation, ct);
        if (channel is null || string.IsNullOrWhiteSpace(channel.BusinessAccountId))
            return BadRequest(ApiResponse<IReadOnlyList<WhatsAppCloudTemplate>>.ErrorResponse("La línea no tiene una cuenta de WhatsApp verificada."));
        var result = await _whatsAppCloudClient.GetMessageTemplatesAsync(channel.BusinessAccountId, channel.AccessToken, ct);
        if (!result.Success) return BadRequest(ApiResponse<IReadOnlyList<WhatsAppCloudTemplate>>.ErrorResponse("No se pudieron consultar las plantillas de esta línea en Meta."));
        return Ok(ApiResponse<IReadOnlyList<WhatsAppCloudTemplate>>.SuccessResponse(result.Templates
            .Where(x => string.Equals(x.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name).ToList()));
    }

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendConversationTemplate(int id,
        SendConversationWhatsAppTemplateDto request, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (conversation is null) return NotFound();
        if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || !await CanAccessConversationAsync(conversation, ct)) return Forbid();
        if (request.Parameters is null || request.Parameters.Count > 20 || request.Parameters.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 1024))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Completa los valores de la plantilla (máximo 1024 caracteres por valor)."));
        var channel = await ResolveConversationChannelAsync(conversation, ct);
        if (channel is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La línea no está conectada."));
        var available = await _whatsAppCloudClient.GetMessageTemplatesAsync(channel.BusinessAccountId, channel.AccessToken, ct);
        var template = available.Success ? available.Templates.SingleOrDefault(x => x.Name == request.TemplateName
            && x.Language == request.Language && string.Equals(x.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)) : null;
        if (template is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La plantilla no está aprobada para esta línea."));
        if (request.Parameters.Count != CountBodyTemplateParameters(template.ComponentsJson))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Los valores no corresponden a esta plantilla."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null || template.Category.Equals("AUTHENTICATION", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(conversation.PhoneNumber))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario válido para esta plantilla."));
        if (!await _db.WhatsAppConversations.AsNoTracking().AnyAsync(x => x.Id == id && x.AssignedUserId == _currentUser.Id
            && x.AttentionMode == WhatsAppAttentionMode.Human && x.AttentionRevision == conversation.AttentionRevision, ct))
            return Conflict(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La atención cambió antes de enviar la plantilla."));
        var sent = await _whatsAppCloudClient.SendTemplateMessageAsync(channel.PhoneNumberId, channel.AccessToken, recipient,
            template.Name, template.Language, request.Parameters, ct);
        RequireKnownOutcome(sent);
        if (!sent.Success) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(sent.ErrorMessage ?? "Meta rechazó el envío."));
        var text = BuildTemplateMessagePreview(new WhatsAppTemplate { Name = template.Name, Components = template.ComponentsJson }, request.Parameters);
        var message = new WhatsAppMessage { TenantId = conversation.TenantId, ConversationId = id,
            WhatsAppMessageId = sent.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = WhatsAppMessageType.Text,
            TextBody = text[..Math.Min(4096, text.Length)], Timestamp = _clock.UtcNow, SentByUserId = _currentUser.Id, Status = WhatsAppMessageStatus.Sent };
        _db.WhatsAppMessages.Add(message); conversation.LastMessageAt = message.Timestamp; conversation.LastMessagePreview = $"Plantilla: {template.Name}";
        await _db.SaveChangesAsync(ct);
        await NotifyWhatsAppMessageCreatedAsync(message.Id, ct);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Plantilla enviada desde la línea de la conversación."));
    }
}
