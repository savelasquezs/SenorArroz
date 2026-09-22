using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class DeliveryStayDetectionWorker : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeliveryStayDetectionWorker> _logger;

    public DeliveryStayDetectionWorker(
        IServiceProvider serviceProvider,
        ILogger<DeliveryStayDetectionWorker> logger)
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
                        var detector = services.GetRequiredService<IDeliveryStayDetectionService>();
                        var processedSessions = await detector.ProcessPendingSessionsAsync(stoppingToken);
                        var classifier = services.GetRequiredService<IDeliveryStayClassificationService>();
                        var classifiedStays = await classifier.ProcessPendingStaysAsync(stoppingToken);
                        var evidenceService = services.GetRequiredService<IDeliveryIncidentEvidenceService>();
                        var incidentsCaptured = await evidenceService.ProcessPendingStaysAsync(stoppingToken);
                        if (processedSessions + classifiedStays + incidentsCaptured > 0)
                            _logger.LogInformation(
                                "Tenant {TenantId}: jornadas {SessionCount}, permanencias {StayCount}, incidentes {IncidentCount}.",
                                tenantId,
                                processedSessions,
                                classifiedStays,
                                incidentsCaptured);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al detectar, clasificar o conservar evidencia de permanencias.");
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
