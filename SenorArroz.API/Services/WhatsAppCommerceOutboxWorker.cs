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
        var cloud = scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var staleBefore = clock.UtcNow.AddMinutes(-5);
        await db.WhatsAppCommerceOutboxMessages.Where(x => x.Status == "processing" && x.UpdatedAt <= staleBefore)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "failed")
                .SetProperty(x => x.LastError, "delivery_unknown_requires_review"), ct);
        var candidateId = await db.WhatsAppCommerceOutboxMessages.AsNoTracking()
            .Where(x => x.Status == "pending" && x.NextAttemptAt <= clock.UtcNow && x.ChannelSetting.IsActive && x.ChannelSetting.IsVerified)
            .OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!candidateId.HasValue) return false;
        var claimed = await db.WhatsAppCommerceOutboxMessages
            .Where(x => x.Id == candidateId && x.Status == "pending" && x.NextAttemptAt <= clock.UtcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "processing")
                .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
        if (claimed == 0) return true;

        var message = await db.WhatsAppCommerceOutboxMessages
            .Include(x => x.ChannelSetting).Include(x => x.Conversation)
            .FirstAsync(x => x.Id == candidateId, ct);
        message.AttemptCount++;
        WhatsAppMessage? sentMessage = null;
        var retrySafe = false;
        try
        {
            var recipient = WhatsAppRecipientResolver.Resolve(message.Conversation)
                ?? throw new InvalidOperationException("La conversación no tiene destinatario.");
            var result = string.IsNullOrWhiteSpace(message.Url)
                ? await cloud.SendTextMessageAsync(message.ChannelSetting.PhoneNumberId, message.ChannelSetting.AccessToken, recipient, message.Body, ct)
                : await cloud.SendUrlButtonMessageAsync(message.ChannelSetting.PhoneNumberId, message.ChannelSetting.AccessToken, recipient, message.Body, message.ButtonText ?? "Abrir", message.Url, ct);
            retrySafe = IsRetrySafe(result.ErrorMessage);
            if (!result.Success) throw new InvalidOperationException("Meta no confirmó el envío.");
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
