using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Services;

public static class WhatsAppMessageStatusTransitions
{
    public static bool ShouldApply(
        WhatsAppMessageStatus current,
        WhatsAppMessageStatus incoming)
    {
        if (current == incoming || current == WhatsAppMessageStatus.Read)
            return false;

        return incoming switch
        {
            WhatsAppMessageStatus.Sent => current == WhatsAppMessageStatus.Received,
            WhatsAppMessageStatus.Delivered => current is
                WhatsAppMessageStatus.Received or
                WhatsAppMessageStatus.Sent or
                WhatsAppMessageStatus.Failed,
            WhatsAppMessageStatus.Read => true,
            WhatsAppMessageStatus.Failed => current is
                WhatsAppMessageStatus.Received or
                WhatsAppMessageStatus.Sent,
            _ => false
        };
    }

    public static bool IsDeliveryProof(WhatsAppMessageStatus status) =>
        status is WhatsAppMessageStatus.Delivered or WhatsAppMessageStatus.Read;
}
