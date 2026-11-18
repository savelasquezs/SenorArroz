using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Services;

public sealed partial class WhatsAppWebhookProcessor
{
    private async Task<bool> ProcessCoexistenceAccountAsync(string? account, JsonElement value, CancellationToken ct)
    {
        if (account is null || TryGetString(value, "event") != "PARTNER_REMOVED") return false;
        var lines = await _db.WhatsAppBranchSettings.Where(x => x.CoexistenceEnabled && x.BusinessAccountId == account).ToListAsync(ct);
        foreach (var line in lines)
        {
            line.IsVerified = false;
            line.ConnectionState = "disconnected";
            line.ConnectionError = "La cuenta se desconectó de la plataforma. Conecta nuevamente desde Meta.";
            var conversations = await _db.WhatsAppConversations.Where(x => x.BranchLineId == line.Id).ToListAsync(ct);
            foreach (var conversation in conversations)
            {
                if (conversation.AttentionMode == WhatsAppAttentionMode.Ai) conversation.AttentionMode = WhatsAppAttentionMode.Paused;
                conversation.AttentionRevision++;
                conversation.AttentionModeUpdatedAt = _clock.UtcNow > conversation.AttentionModeUpdatedAt ? _clock.UtcNow : conversation.AttentionModeUpdatedAt.AddTicks(10);
                conversation.AiPausedAt = _clock.UtcNow;
            }
        }
        return lines.Count > 0;
    }
    private async Task<bool> ProcessCoexistenceAsync(string field, JsonElement value, WhatsAppBranchSetting credentials,
        List<WhatsAppMessage> created, CancellationToken ct)
    {
        var setting = await _db.WhatsAppBranchSettings.SingleAsync(x => x.Id == credentials.Id, ct);
        if (value.TryGetProperty("errors", out _))
        {
            setting.ConnectionState = "degraded";
            setting.ConnectionError = "Meta informó un error de sincronización. Revisa el diagnóstico de la línea.";
            if (field == "history") setting.HistorySyncStatus = "failed";
            return true;
        }
        if (field == "smb_message_echoes" && value.TryGetProperty("message_echoes", out var echoes))
            foreach (var item in echoes.EnumerateArray())
                await ImportCoexistenceMessageAsync(item, WhatsAppWebhookIdentityReader.ReadEchoRecipient(item),
                    false, true, setting, created, ct);
        if (field == "history" && value.TryGetProperty("history", out var chunks))
            foreach (var chunk in chunks.EnumerateArray())
            {
                if (chunk.TryGetProperty("errors", out var errors))
                {
                    setting.HistorySyncStatus = errors.GetRawText().Contains("2593109", StringComparison.Ordinal) ? "declined" : "failed";
                    continue;
                }
                if (setting.HistorySyncStatus != "completed") setting.HistorySyncStatus = "receiving";
                if (chunk.TryGetProperty("threads", out var threads))
                    foreach (var thread in threads.EnumerateArray())
                    {
                        var identity = new WhatsAppInboundIdentity(
                            WhatsAppWebhookIdentityReader.NormalizePhone(TryGetString(thread, "id")), null, null, null);
                        if (!thread.TryGetProperty("messages", out var messages)) continue;
                        foreach (var item in messages.EnumerateArray())
                        {
                            var outbound = !string.IsNullOrWhiteSpace(TryGetString(item, "to"));
                            await ImportCoexistenceMessageAsync(item, identity, true, outbound, setting, created, ct);
                        }
                    }
                if (chunk.TryGetProperty("metadata", out var metadata)
                    && metadata.TryGetProperty("progress", out var progress) && progress.TryGetInt32(out var percent) && percent == 100)
                    setting.HistorySyncStatus = "completed";
            }
        if (field == "smb_app_state_sync" && value.TryGetProperty("state_sync", out var contacts))
        {
            foreach (var item in contacts.EnumerateArray())
            {
                if (TryGetString(item, "type") != "contact" || !item.TryGetProperty("contact", out var contact)) continue;
                var phone = WhatsAppWebhookIdentityReader.NormalizePhone(TryGetString(contact, "phone_number"));
                if (phone is null) continue;
                var conversation = await CoexistenceConversationAsync(setting, phone, ct);
                var name = TryGetString(contact, "full_name") ?? TryGetString(contact, "first_name");
                conversation.ContactName = TryGetString(item, "action") == "remove" || name is null
                    ? null : name[..Math.Min(150, name.Length)];
            }
            setting.ContactsSyncStatus = "received";
        }
        setting.LastSyncAt = _clock.UtcNow;
        if (setting.HistorySyncStatus == "failed") setting.ConnectionState = "degraded";
        else if (setting.HistorySyncStatus is "completed" or "declined") setting.ConnectionState = "connected";
        return true;
    }

    private async Task<WhatsAppConversation> CoexistenceConversationAsync(WhatsAppBranchSetting setting, string phone, CancellationToken ct)
    {
        var conversation = _db.WhatsAppConversations.Local.FirstOrDefault(x => x.TenantId == TenantId
            && x.BranchLineId == setting.Id && x.PhoneNumber == phone)
            ?? await _db.WhatsAppConversations.FirstOrDefaultAsync(x => x.TenantId == TenantId
                && x.BranchLineId == setting.Id && x.PhoneNumber == phone, ct);
        return conversation ?? CreateCoexistenceConversation(setting, phone, null);
    }

    private async Task<WhatsAppConversation> CoexistenceEchoConversationAsync(WhatsAppBranchSetting setting,
        WhatsAppInboundIdentity identity, CancellationToken ct)
    {
        var (byUserId, byPhone) = await FindConversationIdentityAsync(identity, setting, null, ct);
        var phoneConflict = byPhone is not null && identity.UserId is not null
            && !string.IsNullOrWhiteSpace(byPhone.WhatsAppUserId) && byPhone.WhatsAppUserId != identity.UserId;
        var conversation = byUserId ?? (phoneConflict ? null : byPhone);
        if (conversation is null)
            return CreateCoexistenceConversation(setting, phoneConflict ? null : identity.PhoneNumber, identity.UserId);

        if (byPhone is null || ReferenceEquals(byPhone, conversation))
        {
            conversation.PhoneNumber ??= identity.PhoneNumber;
            conversation.WhatsAppUserId = identity.UserId ?? conversation.WhatsAppUserId;
        }
        return conversation;
    }

    private WhatsAppConversation CreateCoexistenceConversation(WhatsAppBranchSetting setting, string? phone, string? userId)
    {
        var conversation = new WhatsAppConversation { TenantId = TenantId, BranchId = setting.BranchId, BranchLineId = setting.Id, PhoneNumber = phone, WhatsAppUserId = userId,
            Status = WhatsAppConversationStatus.Archived,
            AttentionMode = _attentionService.InitialMode(false), AttentionModeUpdatedAt = _clock.UtcNow };
        _db.WhatsAppConversations.Add(conversation);
        return conversation;
    }

    private async Task ImportCoexistenceMessageAsync(JsonElement item, WhatsAppInboundIdentity identity, bool historical, bool outbound,
        WhatsAppBranchSetting setting, List<WhatsAppMessage> created, CancellationToken ct)
    {
        var id = TryGetString(item, "id");
        if (string.IsNullOrWhiteSpace(id) || (identity.PhoneNumber is null && (historical || identity.UserId is null))) return;
        var conversation = historical ? await CoexistenceConversationAsync(setting, identity.PhoneNumber!, ct)
            : await CoexistenceEchoConversationAsync(setting, identity, ct);
        var existing = _db.WhatsAppMessages.Local.FirstOrDefault(x => x.Conversation == conversation && x.WhatsAppMessageId == id)
            ?? await _db.WhatsAppMessages.FirstOrDefaultAsync(x => x.ConversationId == conversation.Id && x.WhatsAppMessageId == id, ct);
        if (existing is not null)
        {
            var historicalMedia = TryGetMediaPayload(item, existing.Type);
            if (historical && existing.IsHistorical && historicalMedia is not null && existing.MediaUrl is null
                && !string.IsNullOrWhiteSpace(historicalMedia.Value.MediaId))
            {
                var stored = await DownloadAndStoreInboundMediaAsync(setting, setting.BranchId, conversation.Id,
                    historicalMedia.Value, existing.Type, ct);
                existing.MediaId = stored?.MediaId ?? historicalMedia.Value.MediaId;
                existing.MediaUrl = stored?.MediaUrl; existing.MediaMimeType = stored?.MimeType ?? historicalMedia.Value.MimeType;
                existing.MediaFileName = stored?.FileName ?? historicalMedia.Value.FileName; existing.MediaFileSize = stored?.FileSize;
            }
            return;
        }
        var timestamp = ParseWhatsAppTimestamp(TryGetString(item, "timestamp")) ?? _clock.UtcNow;
        var content = item;
        var kind = TryGetString(item, "type");
        if (kind is "edit" or "revoke" && item.TryGetProperty(kind, out var change))
        {
            var originalId = TryGetString(change, "original_message_id");
            var original = await _db.WhatsAppMessages.FirstOrDefaultAsync(x => x.ConversationId == conversation.Id
                && x.WhatsAppMessageId == originalId, ct);
            if (kind == "edit" && change.TryGetProperty("message", out var edited)) content = edited;
            if (original is not null)
            {
                original.TextBody = kind == "revoke" ? "Mensaje eliminado desde WhatsApp Business"
                    : TryGetTextBody(content) ?? (content.TryGetProperty(TryGetString(content, "type") ?? "image", out var media)
                        ? TryGetString(media, "caption") : null) ?? original.TextBody;
                if (!historical) created.Add(original);
            }
        }
        var type = TryMapMessageType(TryGetString(content, "type"), out var mapped) ? mapped : WhatsAppMessageType.Text;
        var payload = TryGetMediaPayload(content, type);
        var text = kind == "revoke" ? "Mensaje eliminado desde WhatsApp Business"
            : type == WhatsAppMessageType.Text ? TryGetTextBody(content) : TryGetMediaCaptionOrName(payload, type);
        StoredWhatsAppMedia? mediaFile = null;
        if (payload is not null && !string.IsNullOrWhiteSpace(payload.Value.MediaId))
        {
            if (conversation.Id == 0) await _db.SaveChangesAsync(ct);
            mediaFile = await DownloadAndStoreInboundMediaAsync(setting, setting.BranchId, conversation.Id, payload.Value, type, ct);
        }
        var body = text ?? (type == WhatsAppMessageType.Text ? "Mensaje no compatible" : "Multimedia no disponible");
        var message = new WhatsAppMessage { TenantId = TenantId, Conversation = conversation,
            WhatsAppMessageId = id, Direction = outbound ? WhatsAppMessageDirection.Outbound : WhatsAppMessageDirection.Inbound,
            Type = type, TextBody = body[..Math.Min(body.Length, 4096)],
            Timestamp = timestamp, Status = outbound ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Received,
            Source = historical ? "history" : "businessApp", IsHistorical = historical,
            AiProcessingStatus = WhatsAppAiProcessingStatus.NotApplicable,
            MediaId = mediaFile?.MediaId ?? payload?.MediaId, MediaUrl = mediaFile?.MediaUrl,
            MediaMimeType = mediaFile?.MimeType ?? payload?.MimeType, MediaFileName = mediaFile?.FileName ?? payload?.FileName,
            MediaFileSize = mediaFile?.FileSize, MediaSha256 = mediaFile?.Sha256 ?? payload?.Sha256 };
        if (historical && outbound && item.TryGetProperty("history_context", out var context))
            message.Status = TryGetString(context, "status")?.ToUpperInvariant() switch
            {
                "READ" or "PLAYED" => WhatsAppMessageStatus.Read,
                "DELIVERED" => WhatsAppMessageStatus.Delivered,
                "ERROR" => WhatsAppMessageStatus.Failed,
                _ => WhatsAppMessageStatus.Sent
            };
        _db.WhatsAppMessages.Add(message);
        if (conversation.Status == WhatsAppConversationStatus.Archived) conversation.Status = WhatsAppConversationStatus.Open;
        if (!conversation.LastMessageAt.HasValue || timestamp >= conversation.LastMessageAt)
        {
            conversation.LastMessageAt = timestamp;
            conversation.LastMessagePreview = PreviewForMedia(type, message.TextBody);
        }
        if (historical) return;
        if (conversation.AttentionMode is WhatsAppAttentionMode.Ai or WhatsAppAttentionMode.Closed)
        {
            conversation.AttentionMode = WhatsAppAttentionMode.Paused;
            conversation.Status = WhatsAppConversationStatus.Open;
            conversation.ClosedAt = null;
        }
        conversation.AiPausedAt = _clock.UtcNow;
        conversation.AttentionRevision++;
        conversation.AttentionModeUpdatedAt = _clock.UtcNow;
        conversation.AttentionModeUpdatedByUserId = null;
        if (timestamp >= conversation.LastCustomerMessageAt) conversation.NeedsHumanResponse = false;
        created.Add(message);
    }
}
