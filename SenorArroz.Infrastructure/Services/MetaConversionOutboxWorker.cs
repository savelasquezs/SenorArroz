using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Infrastructure.Services;

public sealed class MetaConversionOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IBackgroundWorkSignal<PaymentNotificationOutboxWork> workSignal,
    ILogger<MetaConversionOutboxWorker> logger) : BackgroundService
{
    private static readonly HashSet<string> PurchaseEventTypes = ["order_created_web_cash", "order_payment_approved"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(5);
            try
            {
                delay = await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falló el procesamiento del outbox de Meta CAPI.");
                delay = TimeSpan.FromSeconds(30);
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

    private async Task<TimeSpan> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<MetaConversionsClient>();
        if (!client.IsConfigured) return TimeSpan.FromMinutes(5);

        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var now = DateTime.UtcNow;
        var messages = await db.PaymentNotificationOutboxMessages
            .Where(x => x.MetaStatus == "pending" && (x.MetaNextAttemptAt == null || x.MetaNextAttemptAt <= now))
            .OrderBy(x => x.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                if (!PurchaseEventTypes.Contains(message.EventType))
                {
                    Ignore(message, now);
                    continue;
                }

                var order = await db.Orders.AsNoTracking()
                    .Include(x => x.Customer)
                    .Include(x => x.OrderDetails)
                    .FirstOrDefaultAsync(x => x.Id == message.OrderId, cancellationToken)
                    ?? throw new InvalidOperationException($"Pedido {message.OrderId} no encontrado para Meta CAPI.");

                if (!string.Equals(order.OrderSource, "web", StringComparison.OrdinalIgnoreCase))
                {
                    Ignore(message, now);
                    continue;
                }

                if (message.EventType == "order_payment_approved")
                {
                    var checkout = await db.StorefrontCheckouts.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
                    if (checkout is not null)
                    {
                        message.MetaConsentGranted = checkout.MetaConsentGranted;
                        message.MetaClientUserAgent ??= checkout.MetaClientUserAgent;
                        message.MetaClientIpAddress ??= checkout.MetaClientIpAddress;
                        message.MetaFbp ??= checkout.MetaFbp;
                        message.MetaFbc ??= checkout.MetaFbc;
                    }
                }

                if (!message.MetaConsentGranted)
                {
                    Ignore(message, now);
                    logger.LogDebug("Meta CAPI omitió el pedido web {OrderId} porque el cliente no autorizó medición.", order.Id);
                    continue;
                }

                var phone = order.Customer?.Phone1;
                if (string.IsNullOrWhiteSpace(phone)) phone = order.Customer?.Phone2;
                if (string.IsNullOrWhiteSpace(phone))
                    throw new InvalidOperationException("El pedido web no tiene un teléfono de cliente disponible para Meta CAPI.");

                var shipping = Math.Max(0, order.DeliveryFee ?? 0);
                var value = Math.Max(0, order.Total - shipping);
                var contents = order.OrderDetails
                    .Where(x => x.Quantity > 0 && x.UnitPrice > 0)
                    .Select(x => new MetaPurchaseContent(x.ProductId, x.Quantity))
                    .ToArray();

                await client.SendPurchaseAsync(new MetaPurchaseEvent(
                    order.Id,
                    message.CreatedAt == default ? now : message.CreatedAt,
                    phone,
                    value,
                    shipping,
                    order.BranchId,
                    message.EventType == "order_created_web_cash" ? "cash" : "online",
                    contents,
                    message.MetaClientUserAgent,
                    message.MetaClientIpAddress,
                    message.MetaFbp,
                    message.MetaFbc), cancellationToken);

                message.MetaStatus = "processed";
                message.MetaProcessedAt = now;
                message.MetaLastError = null;
                message.MetaNextAttemptAt = null;
                logger.LogInformation("Meta CAPI confirmó Purchase para pedido web {OrderId}.", order.Id);
            }
            catch (Exception exception)
            {
                message.MetaAttemptCount++;
                message.MetaLastError = Truncate(exception.Message, 1000);
                message.MetaNextAttemptAt = now.AddSeconds(Math.Min(600, 15 * Math.Pow(2, Math.Min(message.MetaAttemptCount, 5))));
                if (message.MetaAttemptCount >= 10) message.MetaStatus = "failed";
                logger.LogWarning(exception, "No se pudo enviar Purchase de Meta CAPI para pedido {OrderId}.", message.OrderId);
            }
        }

        if (messages.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var nextAttempt = await db.PaymentNotificationOutboxMessages.AsNoTracking()
            .Where(x => x.MetaStatus == "pending")
            .OrderBy(x => x.MetaNextAttemptAt)
            .Select(x => x.MetaNextAttemptAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (!nextAttempt.HasValue) return TimeSpan.FromMinutes(5);
        var delay = nextAttempt.Value - DateTime.UtcNow;
        return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static void Ignore(PaymentNotificationOutboxMessage message, DateTime now)
    {
        message.MetaStatus = "ignored";
        message.MetaProcessedAt = now;
        message.MetaLastError = null;
        message.MetaNextAttemptAt = null;
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
