using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class ExternalDeliveryStatusSyncService(
    IApplicationDbContext db,
    IClock clock,
    IRappiDeliveryProvider rappi) : IExternalDeliveryStatusSyncService
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
    }

    public async Task<bool> SyncCancellationAsync(
        int internalOrderId,
        string reason,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders
            .FirstOrDefaultAsync(x => x.InternalOrderId == internalOrderId, ct);
        if (external is null)
            return false;

        if (external.Status == ExternalOrderStatus.Cancelled)
            return true;

        var result = await rappi.RejectOrderAsync(external.ExternalOrderId, reason, ct);
        external.LastAttemptAt = clock.UtcNow;

        if (!result.Success)
        {
            external.LastError = Limit(result.Error ?? "Rappi rechazó la cancelación.", 1000);
            await db.SaveChangesAsync(ct);
            throw new BusinessException(
                $"Rappi no permitió cancelar el pedido. {external.LastError}");
        }

        external.Status = ExternalOrderStatus.Cancelled;
        external.LastError = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
