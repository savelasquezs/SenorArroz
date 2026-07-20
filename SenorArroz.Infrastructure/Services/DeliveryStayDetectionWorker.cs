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
                await using var scope = _serviceProvider.CreateAsyncScope();
                var detector = scope.ServiceProvider.GetRequiredService<IDeliveryStayDetectionService>();
                var processedSessions = await detector.ProcessPendingSessionsAsync(stoppingToken);
                var classifier = scope.ServiceProvider.GetRequiredService<IDeliveryStayClassificationService>();
                var classifiedStays = await classifier.ProcessPendingStaysAsync(stoppingToken);
                var evidenceService = scope.ServiceProvider.GetRequiredService<IDeliveryIncidentEvidenceService>();
                var incidentsCaptured = await evidenceService.ProcessPendingStaysAsync(stoppingToken);
                if (processedSessions > 0)
                {
                    _logger.LogInformation(
                        "Se analizaron {SessionCount} jornadas con nuevos puntos para detectar permanencias.",
                        processedSessions);
                }
                if (classifiedStays > 0)
                {
                    _logger.LogInformation(
                        "Se clasificaron {StayCount} permanencias de domiciliarios.",
                        classifiedStays);
                }
                if (incidentsCaptured > 0)
                {
                    _logger.LogInformation(
                        "Se actualizo la evidencia de {IncidentCount} incidentes de seguimiento.",
                        incidentsCaptured);
                }
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
