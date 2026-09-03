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
    ILogger<MetaConversionOutboxWorker> logger) : BackgroundService
{
    private static readonly HashSet<string> PurchaseEventTypes = ["order_created_web_cash", "order_payment_approved"];
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;
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
                await Task.Delay(delay, stoppingToken);
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
        if (!client.IsConfigured) return IdleDelay;

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
                        if (checkout.MetaConsentGranted)
                            message.MetaCustomerPhone ??= checkout.CustomerPhone;
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

                var phone = ExactPhone(message, order);
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
        if (!nextAttempt.HasValue) return IdleDelay;
        var delay = nextAttempt.Value - DateTime.UtcNow;
        if (delay <= TimeSpan.Zero) return TimeSpan.Zero;
        return delay < IdleDelay ? delay : IdleDelay;
    }

    private static string ExactPhone(PaymentNotificationOutboxMessage message, Order order)
    {
        if (!string.IsNullOrWhiteSpace(message.MetaCustomerPhone))
            return message.MetaCustomerPhone;

        var candidates = new[] { order.Customer?.Phone1, order.Customer?.Phone2 }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 1)
            return candidates[0];

        throw new InvalidOperationException(
            "No fue posible determinar de forma inequívoca el teléfono verificado del pedido para Meta CAPI.");
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
