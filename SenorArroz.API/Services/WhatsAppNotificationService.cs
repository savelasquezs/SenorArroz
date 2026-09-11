using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.API.Services;

public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IHubContext<WhatsAppHub> _hubContext;
    private readonly ILogger<WhatsAppNotificationService> _logger;
    private readonly IApplicationDbContext _db;

    public WhatsAppNotificationService(
        IHubContext<WhatsAppHub> hubContext,
        IApplicationDbContext db,
        ILogger<WhatsAppNotificationService> logger)
    {
        _hubContext = hubContext;
        _db = db;
        _logger = logger;
    }

    public async Task NotifyMessageCreatedAsync(
        int branchId,
        WhatsAppConversationDto conversation,
        WhatsAppMessageDto message,
        CancellationToken cancellationToken = default)
    {
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var payload = new
        {
            branchId = conversation.OperationalBranchId ?? branchId,
            conversation,
            message
        };

        await _hubContext.Clients
            .Groups(ResolveGroups(branchId, conversation))
            .SendAsync("WhatsAppMessageCreated", payload, cancellationToken);

        _logger.LogInformation(
            "WhatsApp SignalR message emitted. BranchId={BranchId} ConversationId={ConversationId} MessageId={MessageId}",
            branchId,
            conversation.Id,
            message.Id);
    }

    public async Task NotifyAttentionChangedAsync(int branchId, WhatsAppConversationDto conversation, CancellationToken cancellationToken = default)
    {
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var payload = new { branchId = conversation.OperationalBranchId ?? branchId, conversation };
        await _hubContext.Clients.Groups(ResolveGroups(branchId, conversation)).SendAsync("WhatsAppAttentionChanged", payload, cancellationToken);
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
        var conversation = new WhatsAppConversationDto { Id = processing.ConversationId };
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var payload = new { branchId = conversation.OperationalBranchId ?? branchId, processing = realtimeProcessing };
        await _hubContext.Clients
            .Groups(ResolveGroups(branchId, conversation))
            .SendAsync("WhatsAppAiProcessingChanged", payload, cancellationToken);
        _logger.LogInformation(
            "WhatsApp AI processing update emitted. BranchId={BranchId} ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Status={Status} Attempts={Attempts}",
            branchId,
            processing.ConversationId,
            processing.MessageId,
            processing.Status,
            processing.Attempts);
    }

    private static IReadOnlyList<string> ResolveGroups(int legacyBranchId, WhatsAppConversationDto conversation)
    {
        if (!conversation.IsCentralChannel)
            return [$"Branch_{legacyBranchId}_WhatsApp"];
        if (!conversation.OperationalBranchId.HasValue)
            return ["Tenant_1_WhatsApp_Unassigned"];
        return [$"Branch_{conversation.OperationalBranchId.Value}_WhatsApp", "Tenant_1_WhatsApp_Superadmin"];
    }

    private async Task ResolveCentralRoutingAsync(WhatsAppConversationDto conversation, CancellationToken cancellationToken)
    {
        if (conversation.IsCentralChannel) return;
        var routing = await _db.WhatsAppConversations.AsNoTracking()
            .Where(x => x.Id == conversation.Id && x.ChannelSettingId != null)
            .Select(x => new { x.OperationalBranchId, OperationalBranchName = x.OperationalBranch != null ? x.OperationalBranch.Name : null })
            .FirstOrDefaultAsync(cancellationToken);
        if (routing is null) return;
        conversation.IsCentralChannel = true;
        conversation.OperationalBranchId = routing.OperationalBranchId;
        conversation.OperationalBranchName = routing.OperationalBranchName;
    }
}
