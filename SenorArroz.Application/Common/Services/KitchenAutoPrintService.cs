using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public sealed class KitchenAutoPrintService(
    IApplicationDbContext db,
    IPrintQueueService printQueue,
    ILogger<KitchenAutoPrintService> logger) : IKitchenAutoPrintService
{
    public async Task<bool> TryEnqueueAsync(
        Order order,
        KitchenAutoPrintTrigger requiredTrigger,
        CancellationToken cancellationToken = default)
    {
        var settings = await db.BranchPrintSettings
            .AsNoTracking()
            .Where(x => x.BranchId == order.BranchId)
            .Select(x => new { x.EnableKitchenJobs, x.KitchenAutoPrintTrigger })
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "No se encolo comanda automatica para pedido {OrderId}: la sucursal {BranchId} no tiene configuracion de impresion.",
                order.Id,
                order.BranchId);
            return false;
        }

        if (!settings.EnableKitchenJobs || settings.KitchenAutoPrintTrigger != requiredTrigger)
            return false;

        try
        {
            await printQueue.EnqueueAutomaticKitchenAsync(
                order.BranchId,
                order.Id,
                requiredTrigger,
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "No se encolo comanda automatica para pedido {OrderId} (sucursal {BranchId}, evento {Trigger}). El pedido conserva su operacion principal.",
                order.Id,
                order.BranchId,
                requiredTrigger);
            return false;
        }
    }
}
