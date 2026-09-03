using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class ExternalDeliveryStatusSyncService(
    IApplicationDbContext db,
    IClock clock,
    IBackgroundWorkSignal<RappiWork>? workSignal = null) : IExternalDeliveryStatusSyncService
{
    public async Task SyncReadyForPickupAsync(int internalOrderId, CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders
            .Include(x => x.Store)
            .FirstOrDefaultAsync(x => x.InternalOrderId == internalOrderId, ct);
        if (external?.Store?.ManualReadyForPickupEnabled != true)
            return;

        var eventKey = $"READY_FOR_PICKUP:{external.ConnectionId}:{external.ExternalOrderId}";
        if (await db.IntegrationWebhookEvents.AnyAsync(x =>
                x.ConnectionId == external.ConnectionId
                && x.EventKey == eventKey, ct))
            return;

        db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent
        {
            ConnectionId = external.ConnectionId,
            Provider = "rappi",
            EventKey = eventKey,
            EventType = "READY_FOR_PICKUP_OUTBOX",
            PayloadHash = string.Empty,
            PayloadJson = JsonSerializer.Serialize(new
            {
                externalOrderId = external.ExternalOrderId,
                externalDeliveryOrderId = external.Id
            }),
            Status = "outbox_pending",
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);
        workSignal?.Pulse();
    }

}
