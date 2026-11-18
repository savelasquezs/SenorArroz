using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppBranchLineQuery
{
    public static IQueryable<WhatsAppBranchSetting> ForConversation(
        this IQueryable<WhatsAppBranchSetting> settings, WhatsAppConversation conversation)
    {
        var branchLineId = conversation.BranchLineId;
        return settings.Where(x => x.BranchId == conversation.BranchId
            && (branchLineId.HasValue ? x.Id == branchLineId : !x.CoexistenceEnabled));
    }
}
