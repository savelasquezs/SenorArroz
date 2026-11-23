using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Services;

public sealed class WhatsAppCommerceOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WhatsAppCommerceOutboxWorker> logger) : BackgroundService
{
    private sealed record PendingMessage(int Id, int TenantId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try { processed = await ProcessOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "WhatsApp commerce outbox cycle failed."); }
            if (!processed) await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var executionContext = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
        var cloud = scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var pilot = scope.ServiceProvider.GetService<SenorArroz.API.Hosting.WhatsAppCoexistencePilot>();
        var staleBefore = clock.UtcNow.AddMinutes(-5);
        PendingMessage? candidate;
        using (executionContext.BeginSystemScope())
        {
            var messages = db.WhatsAppCommerceOutboxMessages.AsQueryable();
            if (pilot is not null)
                messages = messages.Where(x => x.TenantId == pilot.TenantId
                    && x.ChannelSettingId == null && x.Conversation.BranchId == pilot.BranchId
                    && x.EventKey.StartsWith("whatsapp-order-created:")
                    && db.WhatsAppBranchSettings.Any(line => line.BranchId == pilot.BranchId
                        && line.PhoneNumberId == pilot.PhoneNumberId && line.CoexistenceEnabled
                        && line.CoexistenceConnectedAt < x.CreatedAt));
            await messages.Where(x => x.Status == "processing" && x.UpdatedAt <= staleBefore)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "failed")
                    .SetProperty(x => x.LastError, "delivery_unknown_requires_review"), ct);
            candidate = await messages.AsNoTracking()
                .Where(x => x.Status == "pending" && x.NextAttemptAt <= clock.UtcNow && (x.ChannelSettingId == null || x.ChannelSetting!.IsActive && x.ChannelSetting.IsVerified))
                .OrderBy(x => x.Id)
                .Select(x => new PendingMessage(x.Id, x.TenantId))
                .FirstOrDefaultAsync(ct);
        }
        if (candidate is null) return false;
        int candidateId = candidate.Id;
        int tenantId = candidate.TenantId;
        using var tenantScope = executionContext.BeginTenantScope(tenantId);
        var claimed = await db.WhatsAppCommerceOutboxMessages
            .Where(x => x.Id == candidateId && x.Status == "pending" && x.NextAttemptAt <= clock.UtcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "processing")
                .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
        if (claimed == 0) return true;

        var message = await db.WhatsAppCommerceOutboxMessages
            .Include(x => x.ChannelSetting).Include(x => x.Conversation)
            .FirstAsync(x => x.Id == candidateId, ct);
        if (WhatsAppCommercialDraft.IsLegacyAdvisorConfirmation(message))
        {
            message.Status = "failed";
            message.LastError = "manual_confirmation_disabled";
            await db.SaveChangesAsync(ct);
            return true;
        }
        if (await WhatsAppOperationalContacts.FindAsync(db, message.Conversation, ct) is not null)
        {
            message.Status = "failed";
            message.LastError = WhatsAppOperationalContacts.ManualAttention;
            await db.SaveChangesAsync(ct);
            return true;
        }
        message.AttemptCount++;
        WhatsAppMessage? sentMessage = null;
        var retrySafe = false;
        try
        {
            string phoneNumberId, accessToken;
            if (message.ChannelSetting is { IsActive: true, IsVerified: true } channel)
            { phoneNumberId = channel.PhoneNumberId; accessToken = channel.AccessToken; }
            else if (message.ChannelSettingId is null)
            {
                var branchLine = await db.WhatsAppBranchSettings.AsNoTracking().ForConversation(message.Conversation)
                    .SingleOrDefaultAsync(x => x.IsActive && x.IsVerified, ct)
                    ?? throw new InvalidOperationException("La línea remitente no está disponible.");
                phoneNumberId = branchLine.PhoneNumberId; accessToken = branchLine.AccessToken;
            }
            else throw new InvalidOperationException("La línea remitente no está disponible.");
            var lastCustomerMessage = message.Conversation.LastCustomerMessageAt ?? await db.WhatsAppMessages
                .Where(x => x.ConversationId == message.ConversationId && x.Direction == WhatsAppMessageDirection.Inbound && !x.IsHistorical)
                .MaxAsync(x => (DateTime?)x.Timestamp, ct);
            if (!lastCustomerMessage.HasValue || lastCustomerMessage.Value.AddHours(24) <= clock.UtcNow)
                throw new InvalidOperationException("La ventana de atención cerró. Usa una plantilla desde este chat.");
            var recipient = WhatsAppRecipientResolver.Resolve(message.Conversation)
                ?? throw new InvalidOperationException("La conversación no tiene destinatario.");
            var result = string.IsNullOrWhiteSpace(message.Url)
                ? await cloud.SendTextMessageAsync(phoneNumberId, accessToken, recipient, pilot is null ? message.Body : $"PRUEBA DE DESARROLLO\n{message.Body}", ct)
                : await cloud.SendUrlButtonMessageAsync(phoneNumberId, accessToken, recipient, message.Body, message.ButtonText ?? "Abrir", message.Url, ct);
            retrySafe = pilot is null && IsRetrySafe(result.ErrorMessage);
            if (!result.Success || string.IsNullOrWhiteSpace(result.WhatsAppMessageId)) throw new InvalidOperationException("Meta no confirmó el envío.");
            message.Status = "sent";
            message.SentAt = clock.UtcNow;
            message.LastError = null;
            sentMessage = new WhatsAppMessage
            {
                ConversationId = message.ConversationId,
                WhatsAppMessageId = result.WhatsAppMessageId,
                Direction = WhatsAppMessageDirection.Outbound,
                Type = WhatsAppMessageType.Text,
                TextBody = message.Body,
                Status = WhatsAppMessageStatus.Sent,
                Timestamp = clock.UtcNow,
                AgentDispatchKey = $"commerce:{message.Id}",
                RawPayload = JsonSerializer.Serialize(new { origin = "whatsapp_commerce_outbox", message.EventKey })
            };
            db.WhatsAppMessages.Add(sentMessage);
            message.Conversation.LastMessageAt = clock.UtcNow;
            message.Conversation.LastMessagePreview = message.Body[..Math.Min(500, message.Body.Length)];
        }
        catch (Exception)
        {
            message.LastError = retrySafe ? "provider_rejected_retryable" : "delivery_unknown_or_rejected_requires_review";
            message.Status = retrySafe && message.AttemptCount < 8 ? "pending" : "failed";
            message.NextAttemptAt = clock.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, message.AttemptCount) * 5));
            logger.LogWarning("WhatsApp commerce outbox send failed. OutboxId={OutboxId} Attempt={Attempt}", message.Id, message.AttemptCount);
        }
        await db.SaveChangesAsync(ct);
        if (sentMessage is not null)
        {
            var notifications = scope.ServiceProvider.GetRequiredService<IWhatsAppNotificationService>();
            await notifications.NotifyMessageCreatedAsync(message.Conversation.BranchId, WhatsAppConversationMapper.ToDto(message.Conversation),
                new WhatsAppMessageDto
                {
                    Id = sentMessage.Id, ConversationId = sentMessage.ConversationId, WhatsAppMessageId = sentMessage.WhatsAppMessageId,
                    Direction = "outbound", Type = "text", TextBody = sentMessage.TextBody, Status = "sent",
                    Timestamp = sentMessage.Timestamp, CreatedAt = sentMessage.CreatedAt
                }, ct);
        }
        return true;
    }

    private static bool IsRetrySafe(string? error)
    {
        const string prefix = "Meta WhatsApp HTTP ";
        if (error is null || !error.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var end = error.IndexOf(':', prefix.Length);
        return end > prefix.Length && int.TryParse(error.AsSpan(prefix.Length, end - prefix.Length), out var status)
            && (status is 409 or 429 || status is >= 500 and <= 599);
    }
}
