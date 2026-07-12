using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Domain.Services;

public class WhatsAppAttentionService
{
    public void Take(WhatsAppConversation c, int userId, DateTime now) => Change(c, WhatsAppAttentionMode.Human, userId, now);
    public void ReturnToAi(WhatsAppConversation c, int? userId, DateTime now, bool aiActive) { if (!aiActive) throw new BusinessException("La IA no está configurada y activa para esta sucursal."); Change(c, WhatsAppAttentionMode.Ai, userId, now); }
    public void Pause(WhatsAppConversation c, int userId, DateTime now) => Change(c, WhatsAppAttentionMode.Paused, userId, now);
    public void RequestHuman(WhatsAppConversation c, int? userId, DateTime now) => Change(c, WhatsAppAttentionMode.WaitingForHuman, userId, now);
    public void Close(WhatsAppConversation c, int userId, DateTime now) => Change(c, WhatsAppAttentionMode.Closed, userId, now);
    public void Reopen(WhatsAppConversation c, int userId, DateTime now, bool aiActive) { if (c.AttentionMode != WhatsAppAttentionMode.Closed) throw new BusinessException("Solo una conversación cerrada puede reabrirse."); Apply(c, aiActive ? WhatsAppAttentionMode.Ai : WhatsAppAttentionMode.Human, userId, now); }

    private static void Change(WhatsAppConversation c, WhatsAppAttentionMode target, int? userId, DateTime now)
    {
        if (c.AttentionMode == WhatsAppAttentionMode.Closed) throw new BusinessException("La conversación debe reabrirse antes de cambiar su atención.");
        Apply(c, target, userId, now);
    }
    private static void Apply(WhatsAppConversation c, WhatsAppAttentionMode target, int? userId, DateTime now)
    {
        c.AttentionMode = target; c.AttentionModeUpdatedAt = now; c.AttentionModeUpdatedByUserId = userId;
        c.AssignedUserId = target == WhatsAppAttentionMode.Human ? userId : null;
        c.HumanAssignedAt = target == WhatsAppAttentionMode.Human ? now : null;
        c.AiPausedAt = target is WhatsAppAttentionMode.Paused or WhatsAppAttentionMode.WaitingForHuman ? now : null;
        c.ClosedAt = target == WhatsAppAttentionMode.Closed ? now : null;
    }
}
