using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public static class WhatsAppOrderDraftSessionPolicy
{
    private static readonly WhatsAppOrderDraftStatus[] ActiveStatuses =
    [
        WhatsAppOrderDraftStatus.Building,
        WhatsAppOrderDraftStatus.AwaitingCustomerData,
        WhatsAppOrderDraftStatus.AwaitingAddress,
        WhatsAppOrderDraftStatus.AwaitingPayment,
        WhatsAppOrderDraftStatus.ReadyForConfirmation
    ];

    public static async Task<WhatsAppOrderDraft?> LoadActiveAsync(IApplicationDbContext db,int conversationId,DateTime now,int resumeWindowMinutes,CancellationToken ct=default)
    {
        var candidates=await db.WhatsAppOrderDrafts
            .Include(x=>x.Items).ThenInclude(x=>x.Product)
            .Include(x=>x.Address).ThenInclude(x=>x!.Neighborhood)
            .Include(x=>x.Customer)
            .Include(x=>x.Order)
            .Where(x=>x.ConversationId==conversationId&&ActiveStatuses.Contains(x.Status))
            .OrderByDescending(x=>x.UpdatedAt).ThenByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id)
            .ToListAsync(ct);
        var cutoff=now.AddMinutes(-Math.Max(1,resumeWindowMinutes));
        var selected=candidates.FirstOrDefault(x=>ActivityAt(x)==default||ActivityAt(x)>=cutoff);
        var duplicates=candidates.Where(x=>x!=selected).ToList();
        foreach(var draft in duplicates){draft.Status=WhatsAppOrderDraftStatus.Expired;draft.Version++;}
        if(duplicates.Count>0)await db.SaveChangesAsync(ct);
        return selected;
    }

    private static DateTime ActivityAt(WhatsAppOrderDraft draft)=>draft.UpdatedAt!=default?draft.UpdatedAt:draft.CreatedAt;
}
