using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public class DeliveryWorkSessionAutoCloseService : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(30);
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeliveryWorkSessionAutoCloseService> _logger;

    public DeliveryWorkSessionAutoCloseService(
        IServiceProvider serviceProvider,
        ILogger<DeliveryWorkSessionAutoCloseService> logger)
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
                await CloseExpiredSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cerrar automáticamente las jornadas vencidas.");
            }

            await Task.Delay(Period, stoppingToken);
        }
    }

    private async Task CloseExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var fcm = scope.ServiceProvider.GetRequiredService<IFcmPushService>();
        var nowUtc = ColombiaTimeHelper.EnsureUtc(clock.UtcNow);

        var sessions = await db.DeliveryWorkSessions
            .Where(x => x.Status == DeliveryWorkSessionStatus.Active && x.AutoCloseAt <= nowUtc)
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
            return;

        var deliverymanIds = sessions.Select(x => x.DeliverymanId).Distinct().ToList();
        var deviceTokens = await db.UserDeviceTokens.AsNoTracking()
            .Where(x => deliverymanIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.Token })
            .ToListAsync(cancellationToken);
        var tokensByUser = deviceTokens
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.Select(t => t.Token).ToList());
        var refreshTokens = await db.RefreshTokens
            .Where(x => deliverymanIds.Contains(x.UserId) && !x.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Close(nowUtc, DeliveryWorkSessionEndReason.AutomaticClosure);
            db.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(
                session,
                nowUtc,
                DeliveryWorkSessionEndReason.AutomaticClosure));
        }

        foreach (var refreshToken in refreshTokens)
            refreshToken.Revoke("automatic-work-session-closure", nowUtc);

        await db.SaveChangesAsync(cancellationToken);

        foreach (var deliverymanId in deliverymanIds)
        {
            try
            {
                await fcm.SendToTokensAsync(
                    tokensByUser.GetValueOrDefault(deliverymanId) ?? [],
                    "Jornada finalizada",
                    "Tu jornada terminó en la hora de cierre configurada.",
                    new Dictionary<string, string>
                    {
                        ["type"] = "work_session_closed",
                        ["reason"] = "automatic_closure",
                    },
                    cancellationToken,
                    $"automatic_work_session_closure:{deliverymanId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "La jornada se cerró, pero no se pudo notificar al domiciliario {DeliverymanId}.",
                    deliverymanId);
            }
        }

        _logger.LogInformation(
            "Se cerraron automáticamente {SessionCount} jornadas vencidas.",
            sessions.Count);
    }
}
