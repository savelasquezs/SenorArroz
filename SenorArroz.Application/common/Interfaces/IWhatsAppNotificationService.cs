using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IWhatsAppNotificationService
{
    Task NotifyMessageCreatedAsync(
        int branchId,
        WhatsAppConversationDto conversation,
        WhatsAppMessageDto message,
        CancellationToken cancellationToken = default);
}
