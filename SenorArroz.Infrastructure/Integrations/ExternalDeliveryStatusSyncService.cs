using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class ExternalDeliveryStatusSyncService(
    IApplicationDbContext db,
    IIntegrationSecretProtector protector,
    IRappiDeliveryProvider rappi,
    ILogger<ExternalDeliveryStatusSyncService> logger) : IExternalDeliveryStatusSyncService
{
    public async Task SyncReadyForPickupAsync(int internalOrderId, CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders.Include(x => x.Connection)
            .FirstOrDefaultAsync(x => x.InternalOrderId == internalOrderId && x.Connection.Provider == "rappi", ct);
        if (external is null) return;
        try
        {
            var result = await rappi.ReadyForPickupAsync(external.Connection, protector.Unprotect(external.Connection.EncryptedClientSecret), external.ExternalOrderId, ct);
            if (!result.Success)
            {
                external.Status = ExternalOrderStatus.SyncError; external.LastError = result.Error;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Rappi ready-for-pickup failed for external order {OrderId}: {Error}", external.ExternalOrderId, result.Error);
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Rappi ready-for-pickup failed for internal order {OrderId}", internalOrderId); }
    }
}
