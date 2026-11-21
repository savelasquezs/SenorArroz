using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    [HttpGet("conversations/start/options")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppConversationStartOptions>>> GetConversationStartOptions(
        [FromQuery] int branchId, CancellationToken ct) => _manualOperations.GetConversationStartOptionsAsync(branchId, ct);

    [HttpPost("conversations/start")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [RequestSizeLimit(4096)]
    public Task<ActionResult<ApiResponse<WhatsAppConversationStartResult>>> StartConversation(
        [FromBody] WhatsAppConversationStartRequest request, CancellationToken ct)
    {
        if (_manualQueue is not null)
            return _manualQueue.EnqueueAsync<WhatsAppConversationStartResult>("start-conversation", null,
                JsonSerializer.SerializeToElement(request), Request, ct);
        _manualOperations.ControllerContext = ControllerContext;
        return _manualOperations.StartConversationAsync(request, ct);
    }

    [HttpGet("conversations/{conversationId:int}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppConversationDto>>> GetConversation(int conversationId, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().Include(x => x.ChannelSetting).Include(x => x.BranchLine)
            .Include(x => x.Branch).ThenInclude(x => x.WhatsAppSettings.Where(s => !s.CoexistenceEnabled)).Include(x => x.OperationalBranch)
            .Include(x => x.Customer).Include(x => x.AssignedUser).SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var dto = ToConversationDto(conversation, conversation.AssignedUser?.Name);
        await EnrichCommercialSummariesAsync([dto], ct);
        await EnrichInboxLabelsAsync([dto], InboxDayStart(), ct);
        return Ok(ApiResponse<WhatsAppConversationDto>.SuccessResponse(dto));
    }

    [HttpGet("conversations/{conversationId:int}/templates")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppCloudTemplate>>>> GetConversationTemplates(int conversationId, CancellationToken ct) =>
        _manualOperations.ConversationTemplates(conversationId, ct);

    [HttpPost("conversations/{conversationId:int}/messages/template")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendConversationTemplate(int conversationId,
        [FromBody] SendConversationWhatsAppTemplateDto request, CancellationToken ct)
    {
        if (_manualQueue is not null)
            return _manualQueue.EnqueueAsync<WhatsAppMessageDto>("send-conversation-template", conversationId,
                JsonSerializer.SerializeToElement(request), Request, ct);
        _manualOperations.ControllerContext = ControllerContext;
        return _manualOperations.SendConversationTemplate(conversationId, request, ct);
    }

    [HttpGet("conversations/{conversationId:int}/advisors")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetAdvisors(int conversationId, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var branchId = conversation.ChannelSettingId.HasValue ? conversation.OperationalBranchId : conversation.BranchId;
        var advisors = await _db.Users.AsNoTracking().Where(x => x.Active && x.WebAccessEnabled
            && (x.Role == UserRole.Admin || x.Role == UserRole.Cashier || x.Role == UserRole.Superadmin)
            && (x.Role == UserRole.Superadmin || !branchId.HasValue || x.BranchId == branchId))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.BranchId }).ToListAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(advisors));
    }

    [HttpPost("conversations/{conversationId:int}/attention-actions")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> ExecuteAttentionAction(int conversationId,
        [FromBody] WhatsAppAttentionActionDto request, CancellationToken ct)
    {
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        await WhatsAppCommercialDraft.LockAsync(_db, TenantId, conversationId, ct);
        var conversation = await _db.WhatsAppConversations.SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        if (!request.ExpectedVersion.HasValue)
            return StatusCode(428, ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Actualiza la conversación antes de cambiar su atención."));
        if (request.ExpectedVersion != conversation.AttentionRevision) return await AttentionConflictAsync(conversation, ct);
        var action = request.Action;
        var supervisor = Roles.IsSuperadmin(_currentUser.Role) || Roles.IsAdmin(_currentUser.Role);
        var owner = conversation.AssignedUserId == _currentUser.Id;
        if (action == "force-take")
        {
            if (!supervisor) return Forbid();
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
                return BadRequest(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Indica el motivo de la toma forzada (máximo 500 caracteres)."));
        }
        else if (action == "takeover")
        {
            if (conversation.AttentionMode != WhatsAppAttentionMode.Human || conversation.AssignedUserId is null || owner)
                return await AttentionConflictAsync(conversation, ct);
            if (await _db.WhatsAppWebhookEvents.AnyAsync(x => x.ConversationId == conversationId
                && x.EventType == "manual_operation" && x.ProcessingState == "processing", ct))
                return Conflict(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Hay un envío en curso. Espera a que termine y vuelve a retomar la conversación."));
        }
        else if (action != "take" && action != "reopen" && !owner
            && !(conversation.AssignedUserId is null && action is "pause-ai" or "request-human"))
            return Conflict(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Toma la conversación antes de modificar su atención."));
        if (action == "take" && conversation.AssignedUserId.HasValue && !owner) return await AttentionConflictAsync(conversation, ct);
        if (action == "transfer")
        {
            var branch = conversation.ChannelSettingId.HasValue ? conversation.OperationalBranchId : conversation.BranchId;
            if (!request.TargetUserId.HasValue || !await _db.Users.AnyAsync(x => x.Id == request.TargetUserId && x.Active && x.WebAccessEnabled
                && (x.Role == UserRole.Superadmin || (x.Role == UserRole.Admin || x.Role == UserRole.Cashier)
                    && (!branch.HasValue || x.BranchId == branch)), ct))
                return BadRequest(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("El asesor no tiene acceso a esta conversación."));
        }
        var previousUser = conversation.AssignedUserId;
        var before = new { conversation.AttentionRevision, mode = conversation.AttentionMode.ToString(), conversation.AssignedUserId };
        var aiActive = conversation.ChannelSettingId.HasValue
            ? await _db.TenantAiSettings.AnyAsync(x => x.IsActive && x.IsVerified, ct) : await IsBranchAiActiveAsync(conversation.BranchId, ct);
        var now = _clock.UtcNow;
        bool changed;
        try
        {
            changed = action switch
            {
                "take" => _attentionService.Take(conversation, _currentUser.Id, now),
                "force-take" or "takeover" => _attentionService.ForceTake(conversation, _currentUser.Id, now),
                "release" => _attentionService.Release(conversation, _currentUser.Id, now),
                "transfer" => _attentionService.Transfer(conversation, _currentUser.Id, request.TargetUserId!.Value, now),
                "return-to-ai" => _attentionService.ReturnToAi(conversation, _currentUser.Id, now, aiActive),
                "pause-ai" => _attentionService.Pause(conversation, _currentUser.Id, now),
                "request-human" => _attentionService.RequestHuman(conversation, _currentUser.Id, now),
                "close" => _attentionService.Close(conversation, _currentUser.Id, now),
                "reopen" => _attentionService.Reopen(conversation, _currentUser.Id, now, aiActive),
                _ => throw new BusinessException("Acción de atención no válida.")
            };
        }
        catch (BusinessException e) { return BadRequest(ApiResponse<WhatsAppAttentionDto>.ErrorResponse(e.Message)); }
        var startedDraft = (action is "take" or "takeover" or "force-take" or "transfer")
            && !await _db.WhatsAppCommerceSessions.AnyAsync(x => x.ConversationId == conversationId, ct);
        if (startedDraft) CreateCommercialDraft(conversation, now);
        WhatsAppWebhookEvent? audit = null;
        if (changed)
        {
            audit = new WhatsAppWebhookEvent { TenantId = conversation.TenantId, ConversationId = conversation.Id,
                EventType = "attention_action", ProcessingState = "completed", Processed = true, RequestedByUserId = _currentUser.Id,
                RawPayload = JsonSerializer.Serialize(new { action, before, version = conversation.AttentionRevision,
                    assignedUserId = conversation.AssignedUserId, reason = request.Reason?.Trim() }) };
            _db.WhatsAppWebhookEvents.Add(audit);
        }
        if (changed || startedDraft)
        {
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                if (audit is not null) _db.WhatsAppWebhookEvents.Remove(audit);
                var current = await _db.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == conversationId, ct);
                return await AttentionConflictAsync(current, ct);
            }
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await transaction.DisposeAsync();
        }
        var dto = await ToAttentionDtoAsync(conversation, ct);
        if (changed || startedDraft)
        {
            try
            {
                var summary = ToConversationDto(conversation, dto.AssignedUserName);
                await EnrichCommercialSummariesAsync([summary], ct);
                if (previousUser != conversation.AssignedUserId)
                    await _whatsAppNotificationService.NotifyConversationRoutingChangedAsync(conversation.BranchId,
                        conversation.OperationalBranchId, previousUser, summary, ct);
                await _whatsAppNotificationService.NotifyAttentionChangedAsync(conversation.BranchId, summary, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "WhatsApp attention saved; realtime notification failed. ConversationId={ConversationId}", conversationId); }
        }
        return Ok(ApiResponse<WhatsAppAttentionDto>.SuccessResponse(dto, "Atención actualizada."));
    }

    private async Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> AttentionConflictAsync(WhatsAppConversation conversation, CancellationToken ct)
    {
        var response = ApiResponse<WhatsAppAttentionDto>.ErrorResponse("La conversación cambió. Revisa el responsable y vuelve a intentarlo.");
        response.Data = await ToAttentionDtoAsync(conversation, ct);
        return Conflict(response);
    }
}
