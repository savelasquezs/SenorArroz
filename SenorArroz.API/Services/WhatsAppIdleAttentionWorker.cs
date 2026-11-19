using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Helpers;

namespace SenorArroz.API.Services;

public sealed class WhatsAppIdleAttentionWorker(IServiceScopeFactory scopes,
    ILogger<WhatsAppIdleAttentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await ScanAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "WhatsApp idle attention scan failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> ScanAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tenancy = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var cutoff = clock.UtcNow - WhatsAppIdleAttention.Delay;
        var lastId = 0;
        var closed = 0;
        while (!ct.IsCancellationRequested)
        {
            List<(int Id, int TenantId)> candidates;
            using (tenancy.BeginSystemScope())
            {
                var page = await db.WhatsAppConversations.AsNoTracking()
                    .Where(c => c.Id > lastId && c.AttentionMode == WhatsAppAttentionMode.Human && c.AssignedUserId != null
                        && c.Status != WhatsAppConversationStatus.Archived && !c.IsSupplier && !c.IsPaymentPending
                        && (c.HumanAssignedAt ?? c.AttentionModeUpdatedAt) <= cutoff
                        && (c.LastMessageAt == null || c.LastMessageAt <= cutoff)
                        && db.Orders.Any(o => o.TenantId == c.TenantId && o.WhatsAppConversationId == c.Id
                            && o.Status == OrderStatus.Delivered && o.CreatedAt <= cutoff))
                    .OrderBy(x => x.Id).Take(100).Select(x => new { x.Id, x.TenantId }).ToListAsync(ct);
                candidates = page.Select(x => (x.Id, x.TenantId)).ToList();
            }
            if (candidates.Count == 0) break;
            foreach (var (id, tenantId) in candidates)
            {
                lastId = id;
                try { if (await CloseAsync(id, tenantId, ct)) closed++; }
                catch (Exception ex) when (ex is DbUpdateConcurrencyException || PostgresExceptionHelper.IsTransactionConflict(ex))
                {
                    logger.LogDebug("WhatsApp idle closure postponed after concurrent activity. ConversationId={ConversationId}", id);
                }
            }
        }
        return closed;
    }

    private async Task<bool> CloseAsync(int id, int tenantId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().BeginTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        WhatsAppConversation chat;
        int previousUser;
        await using (var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null)
        {
            await WhatsAppCommercialDraft.LockAsync(db, tenantId, id, ct);
            var candidate = await db.WhatsAppConversations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (candidate is null || !await WhatsAppIdleAttention.EligibleAsync(db, candidate, now, ct)) return false;

            chat = candidate;
            previousUser = chat.AssignedUserId!.Value;
            scope.ServiceProvider.GetRequiredService<WhatsAppAttentionService>().Close(chat, null, now);
            WhatsAppIdleAttention.Audit(db, chat, previousUser, "auto-close-idle", now);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        var notifier = scope.ServiceProvider.GetRequiredService<IWhatsAppNotificationService>();
        await WhatsAppIdleAttention.PublishAsync(notifier, chat, previousUser, logger, ct);
        return true;
    }
}

internal static class WhatsAppIdleAttention
{
    internal static readonly TimeSpan Delay = TimeSpan.FromHours(2);

    internal static bool CouldExpire(WhatsAppConversation chat, DateTime at) =>
        chat.Id > 0 && chat.AttentionMode == WhatsAppAttentionMode.Human && chat.AssignedUserId.HasValue
        && chat.Status != WhatsAppConversationStatus.Archived && !chat.IsSupplier && !chat.IsPaymentPending
        && (chat.HumanAssignedAt ?? chat.AttentionModeUpdatedAt) <= at - Delay;

    internal static async Task<bool> EligibleAsync(IApplicationDbContext db, WhatsAppConversation chat, DateTime at, CancellationToken ct)
    {
        if (!CouldExpire(chat, at) || chat.LastMessageAt > at - Delay || chat.LastCustomerMessageAt > at - Delay)
            return false;

        var orders = db.Orders.AsNoTracking().Where(x => x.WhatsAppConversationId == chat.Id);
        var last = await orders.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.StatusTimes, x.CreatedAt }).FirstOrDefaultAsync(ct);
        if (last?.Status != OrderStatus.Delivered || chat.PendingOrderMarkedAt > last.CreatedAt
            || !Order.ParseStatusTimes(last.StatusTimes).TryGetValue("delivered", out var delivered)
            || delivered < last.CreatedAt || delivered > at - Delay)
            return false;

        if (chat.NeedsHumanResponse)
        {
            var confirmed = await db.WhatsAppCommerceSessions.AsNoTracking()
                .Where(x => x.ConversationId == chat.Id && x.OrderId == last.Id && x.CommercialStatus == "order_taken")
                .MaxAsync(x => x.CompletedAt, ct) ?? last.CreatedAt;
            if (chat.LastCustomerMessageAt is null || chat.LastCustomerMessageAt > confirmed) return false;
        }

        return !await orders.AnyAsync(x => x.Status != OrderStatus.Delivered && x.Status != OrderStatus.Cancelled, ct)
            && !await db.WhatsAppCommerceSessions.AnyAsync(x => x.ConversationId == chat.Id && x.Status == "active", ct)
            && !await db.StorefrontCheckouts.AnyAsync(x => x.WhatsAppConversationId == chat.Id && x.Status == "pending", ct);
    }

    internal static void Audit(IApplicationDbContext db, WhatsAppConversation chat, int previousUser, string action, DateTime now) =>
        db.WhatsAppWebhookEvents.Add(new WhatsAppWebhookEvent
        {
            TenantId = chat.TenantId, ConversationId = chat.Id, EventType = "attention_action",
            ProcessingState = "completed", Processed = true, CreatedAt = now,
            RawPayload = JsonSerializer.Serialize(new { action, previousAssignedUserId = previousUser,
                version = chat.AttentionRevision })
        });

    internal static async Task PublishAsync(IWhatsAppNotificationService notifier, WhatsAppConversation chat,
        int previousUser, ILogger logger, CancellationToken ct)
    {
        try
        {
            var summary = WhatsAppConversationMapper.ToDto(chat);
            await notifier.NotifyConversationRoutingChangedAsync(chat.BranchId, chat.OperationalBranchId, previousUser, summary, ct);
            await notifier.NotifyAttentionChangedAsync(chat.BranchId, summary, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "WhatsApp automatic attention saved; realtime notification failed. ConversationId={ConversationId}", chat.Id);
        }
    }
}
