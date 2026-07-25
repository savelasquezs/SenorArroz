using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.API.Services;

public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IHubContext<WhatsAppHub> _hubContext;
    private readonly ILogger<WhatsAppNotificationService> _logger;

    public WhatsAppNotificationService(
        IHubContext<WhatsAppHub> hubContext,
        ILogger<WhatsAppNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyMessageCreatedAsync(
        int branchId,
        WhatsAppConversationDto conversation,
        WhatsAppMessageDto message,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            branchId,
            conversation,
            message
        };

        await _hubContext.Clients
            .Group($"Branch_{branchId}_WhatsApp")
            .SendAsync("WhatsAppMessageCreated", payload, cancellationToken);

        _logger.LogInformation(
            "WhatsApp SignalR message emitted. BranchId={BranchId} ConversationId={ConversationId} MessageId={MessageId}",
            branchId,
            conversation.Id,
            message.Id);
    }

    public async Task NotifyAttentionChangedAsync(int branchId, WhatsAppConversationDto conversation, CancellationToken cancellationToken = default)
    {
        var payload = new { branchId, conversation };
        await _hubContext.Clients.Group($"Branch_{branchId}_WhatsApp").SendAsync("WhatsAppAttentionChanged", payload, cancellationToken);
    }

    public async Task NotifyAiProcessingChangedAsync(
        int branchId,
        WhatsAppAiProcessingDto processing,
        CancellationToken cancellationToken = default)
    {
        // The branch group also contains cashiers. Keep provider bodies out of the
        // broadcast; authorized admins can retrieve the sanitized detail via REST.
        var realtimeProcessing = new WhatsAppAiProcessingDto
        {
            MessageId = processing.MessageId,
            ConversationId = processing.ConversationId,
            Status = processing.Status,
            Severity = processing.Severity,
            Title = processing.Title,
            Detail = processing.Detail,
            ErrorCategory = processing.ErrorCategory,
            HttpStatusCode = processing.HttpStatusCode,
            Attempts = processing.Attempts,
            MaxAttempts = processing.MaxAttempts,
            WillRetry = processing.WillRetry,
            Timestamp = processing.Timestamp,
            StatusChangedAt = processing.StatusChangedAt,
            StartedAt = processing.StartedAt,
            NextRetryAt = processing.NextRetryAt,
            ProcessedAt = processing.ProcessedAt
        };
        var payload = new { branchId, processing = realtimeProcessing };
        await _hubContext.Clients
            .Group($"Branch_{branchId}_WhatsApp")
            .SendAsync("WhatsAppAiProcessingChanged", payload, cancellationToken);
        _logger.LogInformation(
            "WhatsApp AI processing update emitted. BranchId={BranchId} ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Status={Status} Attempts={Attempts}",
            branchId,
            processing.ConversationId,
            processing.MessageId,
            processing.Status,
            processing.Attempts);
    }
}
