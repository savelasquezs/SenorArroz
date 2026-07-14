using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public class RequestHumanAssistanceAgentTool(
    ApplicationDbContext db,
    WhatsAppAttentionService attention,
    IWhatsAppNotificationService notifications,
    IWhatsAppAutomaticMessageSender sender,
    IClock clock,
    ILogger<RequestHumanAssistanceAgentTool>? logger = null) : IAgentTool
{
    public string Name => "request_human_assistance";
    public string Description => "Transfiere de forma terminal la conversación a un asesor cuando el cliente lo solicita o el caso no puede resolverse.";
    public string Category => "attention";
    public bool ModifiesData => true;
    public string RiskLevel => "high";
    public JsonElement ParametersSchema => JsonDocument.Parse(
        """{"type":"object","properties":{"reason":{"type":"string","maxLength":500}},"required":["reason"],"additionalProperties":false}""")
        .RootElement.Clone();

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var conversation = await db.WhatsAppConversations.FirstOrDefaultAsync(
            x => x.Id == context.ConversationId,
            cancellationToken);
        if (conversation is null)
            return new(false, null, "Conversación no encontrada.", "conversation_not_found");

        if (conversation.AttentionMode != WhatsAppAttentionMode.Ai)
        {
            return new(
                true,
                new { transferred = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman },
                null,
                "human_required",
                TransferredToHuman: true);
        }

        var changed = attention.RequestHuman(conversation, null, clock.UtcNow);
        WhatsAppMessage? incoming = null;
        if (context.IncomingMessageId.HasValue)
        {
            incoming = await db.WhatsAppMessages.FirstOrDefaultAsync(
                x => x.Id == context.IncomingMessageId,
                cancellationToken);
            if (incoming is not null)
            {
                incoming.AiProcessingStatus = WhatsAppAiProcessingStatus.TransferredToHuman;
                incoming.AiProcessingError = arguments.GetProperty("reason").GetString()?.Trim();
                incoming.AiProcessedAt = clock.UtcNow;
                incoming.AiProcessingStartedAt = null;
                incoming.AiNextRetryAt = null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var configured = await db.BranchAiSettings
            .AsNoTracking()
            .Where(x => x.BranchId == conversation.BranchId)
            .Select(x => x.TransferMessage)
            .FirstOrDefaultAsync(cancellationToken);
        var text = string.IsNullOrWhiteSpace(configured)
            ? "Un asesor continuará con tu atención."
            : configured.Trim();
        var dispatch = $"{context.ExecutionId ?? $"msg-{context.IncomingMessageId ?? 0}"}:human-transfer";
        WhatsAppAutomaticSendResult sent;
        try
        {
            sent = await sender.SendTransferTextAsync(
                conversation.Id,
                context.IncomingMessageId ?? 0,
                dispatch,
                text,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(
                ex,
                "WhatsApp human transfer notice failed unexpectedly ConversationId={ConversationId} IncomingMessageId={IncomingMessageId}",
                conversation.Id,
                context.IncomingMessageId);
            sent = new(false, false, null, $"Fallo inesperado al enviar por Meta: {ex.Message}");
        }

        // Realtime feedback is best effort and must never prevent the customer
        // notification from being attempted.
        if (changed)
        {
            try
            {
                await notifications.NotifyAttentionChangedAsync(
                    conversation.BranchId,
                    new WhatsAppConversationDto
                    {
                        Id = conversation.Id,
                        BranchId = conversation.BranchId,
                        PhoneNumber = conversation.PhoneNumber,
                        Status = "open",
                        AttentionMode = "waitingForHuman",
                        AttentionReason = WhatsAppAiDiagnosticsMapper.SanitizeTechnicalDetail(
                            arguments.GetProperty("reason").GetString()?.Trim()),
                        LastMessageAt = conversation.LastMessageAt,
                        LastMessagePreview = conversation.LastMessagePreview,
                        UnreadCount = conversation.UnreadCount,
                        CreatedAt = conversation.CreatedAt,
                        UpdatedAt = conversation.UpdatedAt,
                        AttentionModeUpdatedAt = conversation.AttentionModeUpdatedAt
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Could not emit WhatsApp attention transfer ConversationId={ConversationId}",
                    conversation.Id);
            }
        }

        var warning = sent.Success
            ? null
            : sent.Error ?? "No se pudo enviar el aviso de transferencia.";
        if (incoming is not null && warning is not null)
        {
            var reason = incoming.AiProcessingError ?? "La conversación fue transferida.";
            var diagnostic = $"{reason} | Aviso al cliente no entregado: {warning}";
            incoming.AiProcessingError = diagnostic[..Math.Min(1000, diagnostic.Length)];
            incoming.AiProcessedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new(
            true,
            new { transferred = true, messageSent = sent.Success },
            null,
            "human_required",
            "La conversación fue transferida.",
            TransferredToHuman: true,
            Warnings: warning is null ? null : [warning]);
    }
}
