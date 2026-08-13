using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Services;

public class EmailOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(IServiceScopeFactory scopeFactory, ILogger<EmailOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
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

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessPendingEmailsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<(int Id, Guid PublicId)> tenants;
        using (var discoveryScope = _scopeFactory.CreateScope())
        {
            var context = discoveryScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            tenants = await context.Tenants.AsNoTracking()
                .Where(x => x.Status == TenantStatus.Active)
                .Select(x => new ValueTuple<int, Guid>(x.Id, x.PublicId))
                .ToListAsync(cancellationToken);
        }

        foreach (var tenant in tenants)
        {
            using var scope = _scopeFactory.CreateScope();
            var execution = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
            using var tenantScope = execution.BeginTenantScope(tenant.Id, tenant.PublicId);
            await ProcessTenantEmailsAsync(
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ResendEmailDeliveryService>(),
                scope.ServiceProvider.GetRequiredService<IClock>(),
                cancellationToken);
        }
    }

    private static async Task ProcessTenantEmailsAsync(
        IApplicationDbContext context,
        ResendEmailDeliveryService sender,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var staleProcessingMessages = await context.EmailOutboxMessages
            .Where(x => x.Status == "processing"
                && x.LastAttemptedAt != null
                && x.LastAttemptedAt <= now.AddMinutes(-2)
                && x.AttemptCount < x.MaxAttempts)
            .ToListAsync(cancellationToken);

        foreach (var staleMessage in staleProcessingMessages)
        {
            staleMessage.Status = "retry";
            staleMessage.NextAttemptAt = now;
            staleMessage.LastError = "Recovered stale processing message.";
        }

        if (staleProcessingMessages.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        var messages = await context.EmailOutboxMessages
            .Where(x => (x.Status == "pending" || x.Status == "retry")
                && (x.NextAttemptAt == null || x.NextAttemptAt <= now)
                && x.AttemptCount < x.MaxAttempts)
            .OrderBy(x => x.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Status = "processing";
            message.LastAttemptedAt = now;
            message.AttemptCount += 1;
            await context.SaveChangesAsync(cancellationToken);

            var result = await sender.SendAsync(message, cancellationToken);

            if (result.Success)
            {
                message.Status = "sent";
                message.SentAt = clock.UtcNow;
                message.LastError = null;
                message.NextAttemptAt = null;
            }
            else if (message.AttemptCount >= message.MaxAttempts)
            {
                message.Status = "failed";
                message.LastError = result.ErrorMessage;
                message.NextAttemptAt = null;
            }
            else
            {
                message.Status = "retry";
                message.LastError = result.ErrorMessage;
                message.NextAttemptAt = clock.UtcNow.AddMinutes(Math.Min(Math.Pow(2, message.AttemptCount), 60));
            }

            await UpdateRelatedDispatchAsync(context, message, result, clock.UtcNow, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
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

        dispatch.DispatchStatus = message.AttemptCount >= message.MaxAttempts ? "failed" : "retrying";
        dispatch.DispatchError = $"Provider: {result.Provider}. Error: {result.ErrorMessage}";
        dispatch.DispatchedAt = null;
    }
}
