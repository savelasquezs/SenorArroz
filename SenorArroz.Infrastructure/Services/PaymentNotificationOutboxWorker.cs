using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Infrastructure.Services;

public sealed class PaymentNotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IBackgroundWorkSignal<PaymentNotificationOutboxWork> workSignal,
    ILogger<PaymentNotificationOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(30);
            try
            {
                await ProcessPendingAsync(stoppingToken);
                delay = await GetNextDelayAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falló el procesamiento del outbox de pagos.");
            }

            try
            {
                await workSignal.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<TimeSpan> GetNextDelayAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var nextAttempt = await db.PaymentNotificationOutboxMessages
            .AsNoTracking()
            .Where(x => x.Status == "pending")
            .OrderBy(x => x.NextAttemptAt)
            .Select(x => x.NextAttemptAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (!nextAttempt.HasValue)
            return TimeSpan.FromMinutes(5);
        var delay = nextAttempt.Value - DateTime.UtcNow;
        return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
        var notifications = scope.ServiceProvider.GetRequiredService<IOrderNotificationService>();
        var now = DateTime.UtcNow;
        var messages = await db.PaymentNotificationOutboxMessages
            .Where(x => x.Status == "pending" && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.Id)
            .Take(20)
            .ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                var order = await db.Orders.AsNoTracking()
                    .Include(x => x.Branch)
                    .Include(x => x.TakenBy)
                    .Include(x => x.Customer)
                    .Include(x => x.Address).ThenInclude(x => x!.Neighborhood)
                    .Include(x => x.OrderDetails).ThenInclude(x => x.Product)
                    .FirstOrDefaultAsync(x => x.Id == message.OrderId, cancellationToken);
                if (order is null)
                    throw new InvalidOperationException($"Pedido {message.OrderId} no encontrado.");
                await notifications.NotifyNewOrderToKitchen(mapper.Map<OrderDto>(order));
                message.Status = "processed";
                message.ProcessedAt = now;
                message.LastError = null;
            }
            catch (Exception exception)
            {
                message.AttemptCount++;
                message.LastError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
                message.NextAttemptAt = now.AddSeconds(Math.Min(300, 10 * Math.Pow(2, Math.Min(message.AttemptCount, 5))));
                if (message.AttemptCount >= 10) message.Status = "failed";
                logger.LogWarning(exception, "No se pudo entregar la notificación del pedido pagado {OrderId}.", message.OrderId);
            }
        }
        if (messages.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }
}
