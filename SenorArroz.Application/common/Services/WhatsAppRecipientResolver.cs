using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Services;

public static class WhatsAppRecipientResolver
{
    public static string? Resolve(WhatsAppConversation conversation) =>
        Resolve(conversation.PhoneNumber, conversation.WhatsAppUserId);

    public static string? Resolve(string? phoneNumber, string? whatsAppUserId)
    {
        if (!string.IsNullOrWhiteSpace(phoneNumber))
            return phoneNumber.Trim();
        if (!string.IsNullOrWhiteSpace(whatsAppUserId))
            return whatsAppUserId.Trim();
        return null;
    }
}
