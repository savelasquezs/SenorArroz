using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin, Cashier")]
[Route("api/whatsapp/ai-diagnostics")]
public class WhatsAppAiDiagnosticsController(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IBranchContext branchContext,
    IClock clock,
    IOptions<WhatsAppAiOrchestratorOptions> options) : ControllerBase
{
    private readonly int _maxAttempts = Math.Max(1, options.Value.MaxPersistentAttempts);

    [HttpGet]
    public async Task<ActionResult<ApiResponse<WhatsAppAiDiagnosticsDto>>> Get(
        [FromQuery] int branchId,
        [FromQuery] int? conversationId,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return BadRequest(ApiResponse<WhatsAppAiDiagnosticsDto>.ErrorResponse("La sucursal es requerida."));
        branchContext.EnsureAccess(branchId);
        if (!await db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return NotFound(ApiResponse<WhatsAppAiDiagnosticsDto>.ErrorResponse("Sucursal no encontrada."));

        WhatsAppConversation? conversation = null;
        if (conversationId.HasValue)
        {
            conversation = await db.WhatsAppConversations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == conversationId.Value && x.BranchId == branchId,
                    cancellationToken);
            if (conversation is null)
                return NotFound(ApiResponse<WhatsAppAiDiagnosticsDto>.ErrorResponse("Conversación no encontrada para esta sucursal."));
        }

        var setting = await db.BranchAiSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == branchId, cancellationToken);

        var messagesQuery = db.WhatsAppMessages
            .AsNoTracking()
            .Where(x =>
                x.Conversation.BranchId == branchId
                && x.Direction == WhatsAppMessageDirection.Inbound
                && x.AiProcessingStatus != WhatsAppAiProcessingStatus.NotApplicable);
        if (conversationId.HasValue)
            messagesQuery = messagesQuery.Where(x => x.ConversationId == conversationId.Value);

        var pendingStatuses = new[]
        {
            WhatsAppAiProcessingStatus.Pending,
            WhatsAppAiProcessingStatus.Processing,
            WhatsAppAiProcessingStatus.ResponseGenerated,
            WhatsAppAiProcessingStatus.Sending,
            WhatsAppAiProcessingStatus.Sent
        };
        var now = clock.UtcNow;
        var since = now.AddHours(-24);
        var pendingCount = await messagesQuery.CountAsync(
            x => pendingStatuses.Contains(x.AiProcessingStatus),
            cancellationToken);
        var failedCount = await messagesQuery.CountAsync(
            x => x.AiProcessingStatus == WhatsAppAiProcessingStatus.Failed
                && (x.AiProcessedAt
                    ?? x.AiProcessingStartedAt
                    ?? (x.AiNextRetryAt.HasValue && x.AiNextRetryAt.Value <= now ? x.AiNextRetryAt : null)
                    ?? x.Timestamp) >= since,
            cancellationToken);

        var recentEntities = await messagesQuery
            .OrderByDescending(x =>
                ((x.AiProcessingStatus == WhatsAppAiProcessingStatus.Processing
                  || x.AiProcessingStatus == WhatsAppAiProcessingStatus.ResponseGenerated
                  || x.AiProcessingStatus == WhatsAppAiProcessingStatus.Sending
                  || x.AiProcessingStatus == WhatsAppAiProcessingStatus.Sent)
                 && x.AiProcessingStartedAt.HasValue
                    ? x.AiProcessingStartedAt
                    : x.AiProcessedAt)
                ?? (x.AiNextRetryAt.HasValue && x.AiNextRetryAt.Value <= now ? x.AiNextRetryAt : null)
                ?? x.Timestamp)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(cancellationToken);
        var recent = recentEntities
            .Select(x => WhatsAppAiDiagnosticsMapper.ToDto(x, _maxAttempts, observedAt: now))
            .ToList();

        var response = BuildDiagnostics(branchId, conversation, setting, pendingCount, failedCount, recent);
        if (!Roles.IsAdminOrSuperadmin(currentUser.Role))
            foreach (var item in recent)
                item.TechnicalDetail = null;
        return Ok(ApiResponse<WhatsAppAiDiagnosticsDto>.SuccessResponse(response, "Diagnóstico de IA obtenido."));
    }

    private WhatsAppAiDiagnosticsDto BuildDiagnostics(
        int branchId,
        WhatsAppConversation? conversation,
        BranchAiSetting? setting,
        int pendingCount,
        int failedCount,
        IReadOnlyList<WhatsAppAiProcessingDto> recent)
    {
        var response = new WhatsAppAiDiagnosticsDto
        {
            BranchId = branchId,
            ConversationId = conversation?.Id,
            Provider = setting?.Provider,
            Model = setting?.Model,
            IsActive = setting?.IsActive == true,
            IsVerified = setting?.IsVerified == true,
            AttentionMode = conversation is null ? null : ToAttentionMode(conversation.AttentionMode),
            PendingCount = pendingCount,
            FailedCountLast24Hours = failedCount,
            LastActivityAt = recent.FirstOrDefault()?.StatusChangedAt,
            RecentMessages = recent
        };

        if (setting is null)
        {
            response.AgentStatus = "not_configured";
            response.OverallStatus = "error";
            response.Title = "Agente de IA no configurado";
            response.Summary = "La sucursal recibe mensajes de WhatsApp, pero no tiene un proveedor de IA configurado.";
            return response;
        }

        if (!setting.IsActive)
        {
            response.AgentStatus = "disabled";
            response.OverallStatus = "error";
            response.Title = "Agente de IA deshabilitado";
            response.Summary = "Los mensajes nuevos no serán respondidos automáticamente hasta activar el agente.";
            return response;
        }

        if (!setting.IsVerified)
        {
            response.AgentStatus = "unverified";
            response.OverallStatus = "error";
            response.Title = "Agente de IA sin verificar";
            response.Summary = "La configuración cambió o la prueba de conexión falló; el agente no responderá automáticamente.";
            return response;
        }

        response.AgentStatus = "operational";
        if (conversation is not null && conversation.AttentionMode != WhatsAppAttentionMode.Ai)
        {
            response.OverallStatus = "attention";
            response.Title = conversation.AttentionMode switch
            {
                WhatsAppAttentionMode.WaitingForHuman => "Esperando atención humana",
                WhatsAppAttentionMode.Human => "Conversación atendida por una persona",
                WhatsAppAttentionMode.Paused => "IA pausada en esta conversación",
                WhatsAppAttentionMode.Closed => "Conversación cerrada",
                _ => "La IA no atiende esta conversación"
            };
            response.Summary = "El agente está configurado y operativo, pero esta conversación no está asignada a la IA.";
            return response;
        }

        if (failedCount > 0)
        {
            var failed = recent.FirstOrDefault(x => x.Status == "failed");
            response.OverallStatus = "error";
            response.Title = failed?.Title ?? "La IA tiene respuestas fallidas";
            response.Summary = failed?.Detail
                ?? $"Hay {failedCount} mensaje(s) que no pudieron ser respondidos durante las últimas 24 horas.";
            return response;
        }

        if (pendingCount > 0)
        {
            var retry = recent.FirstOrDefault(x =>
                IsPending(x.Status)
                && x.WillRetry
                && (!string.IsNullOrWhiteSpace(x.ErrorCategory) || x.NextRetryAt.HasValue));
            var pending = retry ?? recent.FirstOrDefault(x => IsPending(x.Status));
            var retrying = retry is not null;
            response.OverallStatus = retrying ? "retrying" : "processing";
            response.Title = pending?.Title ?? (retrying ? "Reintentando respuestas" : "IA procesando mensajes");
            response.Summary = pending?.Detail
                ?? $"Hay {pendingCount} mensaje(s) esperando procesamiento o envío.";
            return response;
        }

        var latest = recent.FirstOrDefault();
        if (latest is null)
        {
            response.OverallStatus = "idle";
            response.Title = "IA operativa";
            response.Summary = "El agente está activo y verificado; todavía no hay actividad reciente para mostrar.";
            return response;
        }

        response.OverallStatus = latest.Status switch
        {
            "pending" when latest.WillRetry && !string.IsNullOrWhiteSpace(latest.ErrorCategory) => "retrying",
            "pending" or "processing" or "responseGenerated" or "sending" or "sent" => "processing",
            "failed" => "error",
            "ignored" when latest.Severity == "error" => "error",
            "transferredToHuman" => "attention",
            "completed" => "healthy",
            _ => "idle"
        };
        response.Title = latest.Title;
        response.Summary = latest.Detail;
        return response;
    }

    private static bool IsPending(string status) => status is
        "pending" or "processing" or "responseGenerated" or "sending" or "sent";

    private static string ToAttentionMode(WhatsAppAttentionMode mode) => mode switch
    {
        WhatsAppAttentionMode.Ai => "ai",
        WhatsAppAttentionMode.Human => "human",
        WhatsAppAttentionMode.WaitingForHuman => "waitingForHuman",
        WhatsAppAttentionMode.Paused => "paused",
        WhatsAppAttentionMode.Closed => "closed",
        _ => "human"
    };
}
