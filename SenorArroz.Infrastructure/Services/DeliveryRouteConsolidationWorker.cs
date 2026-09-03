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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeliveryRouteConsolidationWorker> _logger;
    private readonly IBackgroundWorkSignal<DeliveryRouteConsolidationWork> _workSignal;

    public DeliveryRouteConsolidationWorker(
        IServiceProvider serviceProvider,
        IBackgroundWorkSignal<DeliveryRouteConsolidationWork> workSignal,
        ILogger<DeliveryRouteConsolidationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _workSignal = workSignal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(30);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var workflow = scope.ServiceProvider.GetRequiredService<IDeliveryRouteWorkflowService>();
                var n = await workflow.ConsolidatePendingRoutesAsync(stoppingToken);
                if (n > 0)
                    _logger.LogInformation("Consolidadas {Count} rutas de domicilio.", n);
                var next = await workflow.GetNextPendingConsolidationAtAsync(stoppingToken);
                delay = next.HasValue ? next.Value - DateTime.UtcNow : TimeSpan.FromMinutes(5);
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                if (delay > TimeSpan.FromMinutes(5)) delay = TimeSpan.FromMinutes(5);
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
                await _workSignal.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
