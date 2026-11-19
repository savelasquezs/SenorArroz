using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IWhatsAppNotificationService
{
    Task NotifyInboxScopeChangedAsync(int branchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task NotifyInboxLabelCatalogChangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task NotifyInboxConversationChangedAsync(int conversationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task NotifyMessageCreatedAsync(
        int branchId,
        WhatsAppConversationDto conversation,
        WhatsAppMessageDto message,
        CancellationToken cancellationToken = default);
    Task NotifyAttentionChangedAsync(int branchId, WhatsAppConversationDto conversation, CancellationToken cancellationToken = default);
    Task NotifyConversationRoutingChangedAsync(
        int branchId,
        int? previousOperationalBranchId,
        int? previousAssignedUserId,
        WhatsAppConversationDto conversation,
        CancellationToken cancellationToken = default);
    Task NotifyAiProcessingChangedAsync(
        int branchId,
        WhatsAppAiProcessingDto processing,
        CancellationToken cancellationToken = default);
}
