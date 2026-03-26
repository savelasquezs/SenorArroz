using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

/// <summary>
/// Consolida rutas abiertas tras el delay de asignación (sin intervención del cliente).
/// </summary>
public class DeliveryRouteConsolidationWorker : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeliveryRouteConsolidationWorker> _logger;

    public DeliveryRouteConsolidationWorker(
        IServiceProvider serviceProvider,
        ILogger<DeliveryRouteConsolidationWorker> logger)
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
                using var scope = _serviceProvider.CreateScope();
                var workflow = scope.ServiceProvider.GetRequiredService<IDeliveryRouteWorkflowService>();
                var n = await workflow.ConsolidatePendingRoutesAsync(stoppingToken);
                if (n > 0)
                    _logger.LogInformation("Consolidadas {Count} rutas de domicilio.", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consolidando rutas de domicilio.");
            }

            try
            {
                await Task.Delay(Period, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
