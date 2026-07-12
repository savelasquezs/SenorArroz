using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;

namespace SenorArroz.Tests;

public class WhatsAppAttentionServiceTests
{
    private readonly WhatsAppAttentionService _service = new();
    private static readonly DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void NewConversation_DefaultsToAi() => Assert.Equal(WhatsAppAttentionMode.Ai, new WhatsAppConversation().AttentionMode);
    [Fact] public void Take_AssignsHumanAndUser() { var c = new WhatsAppConversation(); _service.Take(c, 7, Now); Assert.Equal(WhatsAppAttentionMode.Human, c.AttentionMode); Assert.Equal(7, c.AssignedUserId); Assert.Equal(Now, c.HumanAssignedAt); }
    [Fact] public void ReturnToAi_RequiresActiveAi() { var c = new WhatsAppConversation { AttentionMode = WhatsAppAttentionMode.Human }; Assert.Throws<BusinessException>(() => _service.ReturnToAi(c, 7, Now, false)); }
    [Fact] public void Closed_CannotReturnDirectlyToAi() { var c = new WhatsAppConversation { AttentionMode = WhatsAppAttentionMode.Closed }; Assert.Throws<BusinessException>(() => _service.ReturnToAi(c, 7, Now, true)); }
    [Fact] public void Reopen_UsesAiWhenActive() { var c = new WhatsAppConversation { AttentionMode = WhatsAppAttentionMode.Closed }; _service.Reopen(c, 7, Now, true); Assert.Equal(WhatsAppAttentionMode.Ai, c.AttentionMode); Assert.Null(c.ClosedAt); }
    [Fact] public void RequestHuman_PausesAi() { var c = new WhatsAppConversation(); _service.RequestHuman(c, null, Now); Assert.Equal(WhatsAppAttentionMode.WaitingForHuman, c.AttentionMode); Assert.Equal(Now, c.AiPausedAt); }
}
