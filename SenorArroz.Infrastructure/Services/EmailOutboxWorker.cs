using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public class EmailOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailOutboxWorker> _logger;
    private readonly IBackgroundWorkSignal<EmailOutboxWork> _workSignal;

    public EmailOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IBackgroundWorkSignal<EmailOutboxWork> workSignal,
        ILogger<EmailOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _workSignal = workSignal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEmailsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing email outbox.");
            }

            await _workSignal.WaitAsync(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private async Task ProcessPendingEmailsAsync(CancellationToken cancellationToken)
    {
        await TenantWorkerRunner.RunForEachActiveTenantAsync(
            _scopeFactory,
            (services, _) => ProcessTenantEmailsAsync(services, cancellationToken),
            cancellationToken);
    }

    private static async Task ProcessTenantEmailsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var sender = services.GetRequiredService<ResendEmailDeliveryService>();
        var clock = services.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var ids = await context.EmailOutboxMessages.AsNoTracking()
            .Where(x => ((x.Status == "pending" || x.Status == "retry") && x.AttemptCount < x.MaxAttempts
                    && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
                || x.Status == "processing" && (x.LeaseUntil == null ? x.LastAttemptedAt <= now.AddMinutes(-2) : x.LeaseUntil < now)))
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(10).ToListAsync(cancellationToken);
        foreach (var id in ids)
            await ProcessMessageAsync(context, sender, clock, id, cancellationToken);
    }

    public static async Task ProcessMessageAsync(ApplicationDbContext context, ResendEmailDeliveryService sender, IClock clock, int id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var claimed = await context.EmailOutboxMessages.Where(x => x.Id == id
            && (x.Status == "pending" || x.Status == "retry" || x.Status == "processing")
            && (x.LeaseUntil == null || x.LeaseUntil < now)
            && (x.Status == "processing" || x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "processing")
                .SetProperty(x => x.LeaseUntil, now.AddMinutes(2))
                .SetProperty(x => x.FirstAttemptedAt, x => x.FirstAttemptedAt ?? x.LastAttemptedAt ?? now)
                .SetProperty(x => x.LastAttemptedAt, now)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        if (claimed != 1) return;
        var message = await context.EmailOutboxMessages.SingleAsync(x => x.Id == id, ct);
        await context.Entry(message).ReloadAsync(ct);
        var invoice = message.MessageType == "electronic_invoice";
        SenorArroz.Domain.Models.EmailSendResult result;
        if (invoice && message.FirstAttemptedAt <= now.AddHours(-23))
        {
            message.Status = "verifying";
            result = SenorArroz.Domain.Models.EmailSendResult.Fail("resend", "El envío requiere verificación: venció la ventana segura de reintento. No se reenviará automáticamente.");
        }
        else
        {
            result = await sender.SendAsync(message, ct);
            if (result.Success)
            {
                message.Status = "sent"; message.SentAt = clock.UtcNow; message.ProviderMessageId = result.MessageId ?? message.ProviderMessageId;
            }
            else if (result.PermanentFailure || message.AttemptCount >= message.MaxAttempts)
                message.Status = invoice && !result.PermanentFailure ? "verifying" : "failed";
            else message.Status = "retry";
        }
        message.LeaseUntil = null;
        message.LastError = result.Success ? null : result.ErrorMessage;
        message.NextAttemptAt = message.Status == "retry" ? clock.UtcNow.AddMinutes(Math.Min(Math.Pow(2, message.AttemptCount), 60)) : null;
        if (invoice)
        {
            var document = await context.ElectronicInvoiceDocuments.SingleOrDefaultAsync(x => x.Id == message.RelatedEntityId && x.TenantId == message.TenantId, ct);
            if (document is not null)
            {
                document.EmailStatus = message.Status == "sent" ? "sent" : message.Status == "retry" ? "queued" : message.Status;
                document.EmailError = message.LastError; document.EmailSentAt = message.SentAt;
            }
        }
        await UpdateRelatedDispatchAsync(context, message, result, clock.UtcNow, ct);
        await context.SaveChangesAsync(ct);
    }
    private static async Task UpdateRelatedDispatchAsync(
        IApplicationDbContext context,
        SenorArroz.Domain.Entities.EmailOutboxMessage message,
        SenorArroz.Domain.Models.EmailSendResult result,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(message.RelatedEntityType, "daily_audit_dispatch", StringComparison.OrdinalIgnoreCase)
            || !message.RelatedEntityId.HasValue)
            return;

        var dispatch = await context.DailyAuditDispatches.FirstOrDefaultAsync(x => x.Id == message.RelatedEntityId.Value, cancellationToken);
        if (dispatch == null)
            return;

        if (result.Success)
        {
            dispatch.DispatchStatus = "sent";
            dispatch.DispatchError = null;
            dispatch.DispatchedAt = now;
            return;
        }

        dispatch.DispatchStatus = message.Status == "failed" ? "failed" : "retrying";
        dispatch.DispatchError = $"Provider: {result.Provider}. Error: {result.ErrorMessage}";
        dispatch.DispatchedAt = null;
    }
}
