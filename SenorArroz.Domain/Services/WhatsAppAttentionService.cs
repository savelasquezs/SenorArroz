using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Domain.Services;

public class WhatsAppAttentionService
{
    public WhatsAppAttentionMode InitialMode(bool aiActiveAndVerified) => aiActiveAndVerified ? WhatsAppAttentionMode.Ai : WhatsAppAttentionMode.Human;
    public bool Take(WhatsAppConversation c, int userId, DateTime now)
    {
        EnsureNotClosed(c);
        if (c.AttentionMode == WhatsAppAttentionMode.Human && c.AssignedUserId == userId) return false;
        Apply(c, WhatsAppAttentionMode.Human, userId, now); return true;
    }
    public bool ReturnToAi(WhatsAppConversation c, int? userId, DateTime now, bool aiActive)
    {
        EnsureNotClosed(c); if (c.AttentionMode == WhatsAppAttentionMode.Ai) return false;
        if (!aiActive) throw new BusinessException("La IA no está configurada, activa y verificada para esta sucursal.");
        Apply(c, WhatsAppAttentionMode.Ai, userId, now); return true;
    }
    public bool Pause(WhatsAppConversation c, int userId, DateTime now) => ChangeIdempotent(c, WhatsAppAttentionMode.Paused, userId, now);
    public bool RequestHuman(WhatsAppConversation c, int? userId, DateTime now) => ChangeIdempotent(c, WhatsAppAttentionMode.WaitingForHuman, userId, now);
    public bool Close(WhatsAppConversation c, int userId, DateTime now)
    {
        if (c.AttentionMode == WhatsAppAttentionMode.Closed) return false;
        Apply(c, WhatsAppAttentionMode.Closed, userId, now); return true;
    }
    public bool Reopen(WhatsAppConversation c, int userId, DateTime now, bool aiActive)
    {
        if (c.AttentionMode != WhatsAppAttentionMode.Closed) throw new BusinessException("Solo una conversación cerrada puede reabrirse.");
        Apply(c, aiActive ? WhatsAppAttentionMode.Ai : WhatsAppAttentionMode.Human, userId, now); return true;
    }
    private static bool ChangeIdempotent(WhatsAppConversation c, WhatsAppAttentionMode target, int? userId, DateTime now)
    { EnsureNotClosed(c); if (c.AttentionMode == target) return false; Apply(c, target, userId, now); return true; }
    private static void EnsureNotClosed(WhatsAppConversation c) { if (c.AttentionMode == WhatsAppAttentionMode.Closed) throw new BusinessException("La conversación debe reabrirse antes de cambiar su atención."); }
    private static void Apply(WhatsAppConversation c, WhatsAppAttentionMode target, int? userId, DateTime now)
    {
        c.AttentionMode = target; c.AttentionModeUpdatedAt = now; c.AttentionModeUpdatedByUserId = userId;
        c.AssignedUserId = target == WhatsAppAttentionMode.Human ? userId : null;
        c.HumanAssignedAt = target == WhatsAppAttentionMode.Human ? now : null;
        c.AiPausedAt = target is WhatsAppAttentionMode.Paused or WhatsAppAttentionMode.WaitingForHuman ? now : null;
        c.ClosedAt = target == WhatsAppAttentionMode.Closed ? now : null;
    }
}
