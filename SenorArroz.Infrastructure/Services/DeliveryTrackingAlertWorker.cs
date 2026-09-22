using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class DeliveryTrackingAlertWorker : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeliveryTrackingAlertWorker> _logger;

    public DeliveryTrackingAlertWorker(
        IServiceProvider serviceProvider,
        ILogger<DeliveryTrackingAlertWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TenantWorkerRunner.RunForEachActiveTenantAsync(
                    _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                    async (services, tenantId) =>
                    {
                        var changes = await services.GetRequiredService<IDeliveryTrackingAlertService>()
                            .ProcessAsync(stoppingToken);
                        if (changes > 0)
                            _logger.LogInformation(
                                "Se procesaron {AlertChanges} cambios de alertas de seguimiento para tenant {TenantId}.",
                                changes,
                                tenantId);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar alertas de seguimiento de domiciliarios.");
            }

            try
            {
                await Task.Delay(Period, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
