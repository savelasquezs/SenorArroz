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

        await _hubContext.Clients
            .Group("WhatsApp_Superadmin")
            .SendAsync("WhatsAppMessageCreated", payload, cancellationToken);

        _logger.LogInformation(
            "WhatsApp SignalR message emitted. BranchId={BranchId} ConversationId={ConversationId} MessageId={MessageId}",
            branchId,
            conversation.Id,
            message.Id);
    }
}
