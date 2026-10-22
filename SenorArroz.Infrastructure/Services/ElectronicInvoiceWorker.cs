using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Services;

public sealed class ElectronicInvoiceWorker(IServiceScopeFactory scopes, ILogger<ElectronicInvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TenantWorkerRunner.RunForEachActiveTenantAsync(scopes, async (services, _) =>
                {
                    var db = services.GetRequiredService<IApplicationDbContext>();
                    var clock = services.GetRequiredService<IClock>();
                    var ids = await db.ElectronicInvoiceDocuments.AsNoTracking().Where(x =>
                        (x.NextAttemptAt == null || x.NextAttemptAt <= clock.UtcNow) && (x.LeaseUntil == null || x.LeaseUntil < clock.UtcNow)
                        && (x.Status == "pending" || x.Status == "scheduled" || x.Status == "processing" || x.Status == "verifying"
                            || (x.Status == "issued" || x.Status == "credited") && (x.Xml == null || x.Pdf == null || x.EmailStatus == "pending"
                                || x.Kind == "invoice" && !x.CreditRequested && db.Orders.Any(o => o.Id == x.OrderId && o.Status == OrderStatus.Cancelled))))
                        .OrderBy(x => x.Id).Select(x => x.Id).Take(20).ToListAsync(stoppingToken);
                    foreach (var id in ids)
                        await services.GetRequiredService<IElectronicInvoiceService>().ProcessAsync(id, stoppingToken);
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Error procesando documentos electrónicos."); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
