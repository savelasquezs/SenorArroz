using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed class WhatsAppRealtimeRelay(IServiceScopeFactory scopes, IHubContext<WhatsAppHub> hub,
    IOptions<WhatsAppAiOrchestratorOptions> aiOptions, ILogger<WhatsAppRealtimeRelay> logger,
    WhatsAppDatabaseSignal? signal = null) : BackgroundService
{
    private readonly string _consumer = Guid.NewGuid().ToString("N");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        using var system = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>().BeginSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE whatsapp_realtime_event SET delivered_to = array_append(delivered_to, {_consumer})
            WHERE NOT ({_consumer} = ANY(delivered_to))
            """, ct);
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var version = signal?.Version ?? 0;
            try
            {
                var count = await RelayOnceAsync(ct);
                if (count == 100) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "WhatsApp realtime relay cycle failed."); }
            try
            {
                if (signal is not null) await signal.WaitAsync(version, ct);
                else await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    public async Task<int> RelayOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        List<Domain.Entities.WhatsAppRealtimeEvent> events;
        using (execution.BeginSystemScope())
            events = await db.WhatsAppRealtimeEvents.AsNoTracking().Where(x => !x.DeliveredTo.Contains(_consumer))
                .OrderBy(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var item in events)
        {
            using var tenant = execution.BeginTenantScope(item.TenantId);
            var conversation = await db.WhatsAppConversations.AsNoTracking().Include(x => x.Branch)
                .Include(x => x.OperationalBranch).Include(x => x.Customer).Include(x => x.AssignedUser)
                .SingleOrDefaultAsync(x => x.Id == item.ConversationId, ct);
            if (conversation is not null)
            {
                var dto = WhatsAppConversationMapper.ToDto(conversation, conversation.AssignedUser?.Name);
                var currentGroups = WhatsAppRealtimeGroupResolver.Resolve(item.TenantId, conversation.BranchId, dto);
                var allowed = currentGroups.Intersect(item.Groups).ToArray();
                var payload = JsonNode.Parse(item.Payload)!.AsObject();
                payload["branchId"] = conversation.OperationalBranchId ?? conversation.BranchId;
                if (payload.ContainsKey("conversation")) payload["conversation"] = JsonSerializer.SerializeToNode(dto, JsonOptions);
                if (payload["processing"] is JsonObject processing && processing["messageId"] is not null)
                {
                    var messageId = processing["messageId"]!.GetValue<int>();
                    var message = await db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(x =>
                        x.Id == messageId && x.ConversationId == item.ConversationId, ct);
                    if (message is not null) payload["processing"] = JsonSerializer.SerializeToNode(
                        WhatsAppAiDiagnosticsMapper.ToDto(message, aiOptions.Value.MaxPersistentAttempts), JsonOptions);
                }
                if (payload["message"] is JsonObject messagePayload && messagePayload["id"] is not null)
                {
                    var messageId = messagePayload["id"]!.GetValue<int>();
                    var message = await db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(x =>
                        x.Id == messageId && x.ConversationId == item.ConversationId, ct);
                    if (message is not null) messagePayload["status"] = message.Status.ToString().ToLowerInvariant();
                }
                if (allowed.Length > 0)
                    await hub.Clients.Groups(allowed).SendAsync(item.EventName, JsonSerializer.SerializeToElement(payload), ct);
                if (item.EventName == "WhatsAppConversationRoutingChanged")
                {
                    var removed = item.Groups.Except(currentGroups).ToArray();
                    if (removed.Length > 0)
                        await hub.Clients.Groups(removed).SendAsync(item.EventName, new
                        {
                            branchId = conversation.OperationalBranchId ?? conversation.BranchId,
                            conversation = new { id = conversation.Id, isCentralChannel = dto.IsCentralChannel,
                                operationalBranchId = conversation.OperationalBranchId, assignedUserId = conversation.AssignedUserId }
                        }, ct);
                }
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE whatsapp_realtime_event SET delivered_to = array_append(delivered_to, {_consumer})
                WHERE id = {item.Id} AND tenant_id = {item.TenantId} AND NOT ({_consumer} = ANY(delivered_to))
                """, ct);
        }
        return events.Count;
    }
}
