using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class WhatsAppOrderDraftSession(
    ApplicationDbContext db,
    IOptions<WhatsAppOrderDraftOptions> options,
    IClock clock) : IWhatsAppOrderDraftSession
{
    public Task<WhatsAppOrderDraft?> LoadActiveAsync(int conversationId, CancellationToken ct = default) =>
        WhatsAppOrderDraftSessionPolicy.LoadActiveAsync(db,conversationId,clock.UtcNow,options.Value.ResumeWindowMinutes,ct);

    public async Task<WhatsAppOrderDraft> GetOrCreateAsync(AgentToolExecutionContext context, CancellationToken ct = default)
    {
        var current = await LoadActiveAsync(context.ConversationId, ct);
        if (current is not null) return current;
        var draft = new WhatsAppOrderDraft
        {
            ConversationId = context.ConversationId,
            BranchId = context.BranchId,
            CustomerId = context.CustomerId,
            OrderType = OrderType.Delivery,
            Status = context.CustomerId.HasValue ? WhatsAppOrderDraftStatus.Building : WhatsAppOrderDraftStatus.AwaitingCustomerData,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.WhatsAppOrderDrafts.Add(draft);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(draft).State = EntityState.Detached;
            var existing = await LoadActiveAsync(context.ConversationId, ct);
            if (existing is null) throw;
            return existing;
        }
        return draft;
    }

}
