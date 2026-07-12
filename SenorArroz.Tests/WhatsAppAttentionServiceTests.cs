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
    [Fact] public void InitialMode_WithActiveVerifiedAi_IsAi() => Assert.Equal(WhatsAppAttentionMode.Ai, _service.InitialMode(true));
    [Fact] public void InitialMode_WithoutActiveVerifiedAi_IsHuman() => Assert.Equal(WhatsAppAttentionMode.Human, _service.InitialMode(false));
    [Theory]
    [InlineData(WhatsAppAttentionMode.Ai)] [InlineData(WhatsAppAttentionMode.Paused)] [InlineData(WhatsAppAttentionMode.WaitingForHuman)] [InlineData(WhatsAppAttentionMode.Closed)]
    public void RepeatingIdempotentTransition_DoesNotChangeAudit(WhatsAppAttentionMode mode)
    {
        var old = Now.AddHours(-1); var c = new WhatsAppConversation { AttentionMode = mode, AttentionModeUpdatedAt = old, AttentionModeUpdatedByUserId = 3 };
        var changed = mode switch { WhatsAppAttentionMode.Ai => _service.ReturnToAi(c, 7, Now, true), WhatsAppAttentionMode.Paused => _service.Pause(c, 7, Now), WhatsAppAttentionMode.WaitingForHuman => _service.RequestHuman(c, 7, Now), _ => _service.Close(c, 7, Now) };
        Assert.False(changed); Assert.Equal(old, c.AttentionModeUpdatedAt); Assert.Equal(3, c.AttentionModeUpdatedByUserId);
    }
    [Fact] public void Take_BySameUser_IsIdempotent() { var c = new WhatsAppConversation { AttentionMode = WhatsAppAttentionMode.Human, AssignedUserId = 7, AttentionModeUpdatedAt = Now.AddHours(-1) }; Assert.False(_service.Take(c, 7, Now)); Assert.NotEqual(Now, c.AttentionModeUpdatedAt); }
    [Fact] public void Take_ByAnotherUser_Reassigns() { var c = new WhatsAppConversation { AttentionMode = WhatsAppAttentionMode.Human, AssignedUserId = 3 }; Assert.True(_service.Take(c, 7, Now)); Assert.Equal(7, c.AssignedUserId); Assert.Equal(Now, c.AttentionModeUpdatedAt); }
}
