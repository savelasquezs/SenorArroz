using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Services;

public sealed class WhatsAppWebhookProcessor
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IWhatsAppCloudClient _whatsAppCloudClient;
    private readonly IWhatsAppNotificationService _whatsAppNotificationService;
    private readonly IFirebaseGcsStorage _firebaseStorage;
    private readonly FirebaseStorageOptions _firebaseOptions;
    private readonly ILogger<WhatsAppWebhookProcessor> _logger;
    private readonly WhatsAppAttentionService _attentionService;
    private readonly IWhatsAppAiWorkQueue _aiWorkQueue;
    private readonly IWhatsAppAutomaticMessageSender _automaticMessageSender;
    private readonly IBranchBusinessHoursService _businessHoursService;
    private readonly WhatsAppAwayMessageService _awayMessageService;
    private readonly WhatsAppCommerceFlowService _commerceFlow;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IInventoryService? _inventory;
    private int TenantId => _currentTenant?.TenantId ?? _commerceFlow.TenantId;
    private readonly int _aiMaxPersistentAttempts;
    private readonly Lazy<IAgentToolExecutor>? _toolExecutor;
    private readonly bool _externalProcessing;

    public WhatsAppWebhookProcessor(
        IApplicationDbContext db,
        IClock clock,
        IWhatsAppCloudClient whatsAppCloudClient,
        IWhatsAppNotificationService whatsAppNotificationService,
        IFirebaseGcsStorage firebaseStorage,
        IOptions<FirebaseStorageOptions> firebaseOptions,
        IOptions<WhatsAppAiOrchestratorOptions> aiOptions,
        ILogger<WhatsAppWebhookProcessor> logger,
        WhatsAppAttentionService attentionService,
        IWhatsAppAiWorkQueue aiWorkQueue,
        IWhatsAppAutomaticMessageSender automaticMessageSender,
        IBranchBusinessHoursService businessHoursService,
        WhatsAppAwayMessageService awayMessageService,
        WhatsAppCommerceFlowService commerceFlow,
        ICurrentTenant? currentTenant = null,
        IInventoryService? inventory = null,
        Lazy<IAgentToolExecutor>? toolExecutor = null,
        IOptions<WhatsAppProcessingOptions>? processing = null)
    {
        _db = db;
        _clock = clock;
        _whatsAppCloudClient = whatsAppCloudClient;
        _whatsAppNotificationService = whatsAppNotificationService;
        _firebaseStorage = firebaseStorage;
        _firebaseOptions = firebaseOptions.Value;
        _aiMaxPersistentAttempts = Math.Max(1, aiOptions.Value.MaxPersistentAttempts);
        _logger = logger;
        _attentionService = attentionService;
        _aiWorkQueue = aiWorkQueue;
        _automaticMessageSender = automaticMessageSender;
        _businessHoursService = businessHoursService;
        _awayMessageService = awayMessageService;
        _commerceFlow = commerceFlow;
        _currentTenant = currentTenant;
        _inventory = inventory;
        _toolExecutor = toolExecutor;
        _externalProcessing = processing?.Value.External == true;
    }

    public async Task ProcessAsync(WhatsAppWebhookEvent webhookEvent, string rawPayload, CancellationToken cancellationToken)
    {
        var createdMessages = new List<WhatsAppMessage>();
        var aiProcessingChanges = new List<WhatsAppMessage>();
        var awayMessageDispatches = new List<WhatsAppAwayMessageDispatch>();
        using var document = JsonDocument.Parse(rawPayload);
        webhookEvent.Processed = await ProcessWebhookPayloadAsync(document.RootElement, webhookEvent,
            createdMessages, aiProcessingChanges, awayMessageDispatches, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
            foreach (var message in createdMessages.Where(x => x.Id > 0))
            {
                await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
                if (message.Conversation.ChannelSettingId.HasValue
                    && WhatsAppCommerceFlowService.IsPaymentRetryIntent(message.TextBody)
                    && await RetryPaymentAsync(message, cancellationToken))
                {
                    message.AiProcessingStatus = WhatsAppAiProcessingStatus.Ignored;
                    message.AiProcessedAt = _clock.UtcNow;
                    message.AiProcessingError = "whatsapp_payment_retry";
                    await _db.SaveChangesAsync(cancellationToken);
                    continue;
                }
                if (message.Conversation.ChannelSettingId.HasValue
                    && (WhatsAppCommerceFlowService.IsPurchaseIntent(message.TextBody) || WhatsAppCommerceFlowService.IsGreeting(message.TextBody))
                    && await _commerceFlow.StartAsync(message.ConversationId, message.Conversation.ChannelSettingId.Value, cancellationToken,
                        greeting: WhatsAppCommerceFlowService.IsGreeting(message.TextBody)))
                {
                    message.AiProcessingStatus = WhatsAppAiProcessingStatus.Ignored;
                    message.AiProcessedAt = _clock.UtcNow;
                    message.AiProcessingError = "whatsapp_flow_started";
                    await _db.SaveChangesAsync(cancellationToken);
                    await NotifyWhatsAppAiProcessingChangedAsync(message.Id, cancellationToken);
                    var flowMessageId = await _db.WhatsAppMessages.Where(x => x.ConversationId == message.ConversationId
                            && x.Direction == WhatsAppMessageDirection.Outbound && x.Id > message.Id)
                        .OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
                    if (flowMessageId.HasValue)
                        await NotifyWhatsAppMessageCreatedAsync(flowMessageId.Value, cancellationToken);
                    continue;
                }
                var awayDispatch = awayMessageDispatches.FirstOrDefault(x => ReferenceEquals(x.Message, message));
                if (awayDispatch is not null)
                {
                    var dispatchKey = WhatsAppAwayMessageService.BuildDispatchKey(
                        message.ConversationId,
                        awayDispatch.ClosedPeriodStartedAtUtc);
                    var sendResult = await _automaticMessageSender.SendAwayTextAsync(
                        message.ConversationId,
                        dispatchKey,
                        awayDispatch.Text,
                        cancellationToken);
                    if (!sendResult.Success)
                    {
                        _logger.LogWarning(
                            "WhatsApp away message could not be sent. ConversationId={ConversationId} MessageId={MessageId} Error={Error}",
                            message.ConversationId,
                            message.Id,
                            sendResult.Error);
                    }
                    continue;
                }
                if (!_externalProcessing && !_aiWorkQueue.TryEnqueue(message.ConversationId, message.Id))
                    _logger.LogWarning("WhatsApp AI queue full; message remains pending. ConversationId={ConversationId} MessageId={MessageId}", message.ConversationId, message.Id);
            }
            foreach (var messageId in aiProcessingChanges.Select(x => x.Id).Where(x => x > 0).Distinct())
                await NotifyWhatsAppAiProcessingChangedAsync(messageId, cancellationToken);
    }

    private Task<bool> RetryPaymentAsync(WhatsAppMessage message, CancellationToken ct) =>
        _toolExecutor?.Value is WhatsAppBusinessGateway gateway
            ? gateway.RetryPaymentAsync(message.Id, ct)
            : _commerceFlow.RetryPaymentAsync(message.ConversationId, message.Id, ct);

    private sealed record WhatsAppAwayMessageDispatch(
        WhatsAppMessage Message,
        DateTime ClosedPeriodStartedAtUtc,
        string Text);

    internal async Task NotifyWhatsAppMessageCreatedAsync(int messageId, CancellationToken cancellationToken)
    {
        var message = await _db.WhatsAppMessages
            .AsNoTracking()
            .Include(x => x.Conversation)
                .ThenInclude(x => x.Branch)
            .Include(x => x.Conversation)
                .ThenInclude(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);

        if (message?.Conversation is null)
            return;

        await _whatsAppNotificationService.NotifyMessageCreatedAsync(
            message.Conversation.BranchId,
            ToConversationDto(message.Conversation),
            ToMessageDto(message),
            cancellationToken);
    }

    private async Task NotifyWhatsAppAiProcessingChangedAsync(int messageId, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _db.WhatsAppMessages
                .AsNoTracking()
                .Include(x => x.Conversation)
                .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);
            if (message?.Conversation is null)
                return;

            await _whatsAppNotificationService.NotifyAiProcessingChangedAsync(
                message.Conversation.BranchId,
                WhatsAppAiDiagnosticsMapper.ToDto(message, _aiMaxPersistentAttempts),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The webhook request is ending; the persisted state remains available through REST.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not emit WhatsApp AI delivery status IncomingMessageId={IncomingMessageId}",
                messageId);
        }
    }

    private async Task<bool> ProcessWebhookPayloadAsync(
        JsonElement root,
        WhatsAppWebhookEvent webhookEvent,
        List<WhatsAppMessage> createdMessages,
        List<WhatsAppMessage> aiProcessingChanges,
        List<WhatsAppAwayMessageDispatch> awayMessageDispatches,
        CancellationToken cancellationToken)
    {
        var processedAny = false;
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                    continue;

                var phoneNumberId = TryGetPhoneNumberId(value);
                if (string.IsNullOrWhiteSpace(phoneNumberId))
                    continue;

                var channel = await _db.WhatsAppChannelSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PhoneNumberId == phoneNumberId && x.IsActive && x.IsVerified, cancellationToken);
                var centralBranchId = channel is null ? 0 : await _db.Branches.AsNoTracking()
                    .Where(x => x.TenantId == TenantId).OrderByDescending(x => x.IsActive).ThenBy(x => x.Id)
                    .Select(x => x.Id).FirstAsync(cancellationToken);
                var setting = channel is null
                    ? await _db.WhatsAppBranchSettings.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.PhoneNumberId == phoneNumberId && x.IsActive && x.IsVerified, cancellationToken)
                    : new WhatsAppBranchSetting
                    {
                        BranchId = centralBranchId,
                        PhoneNumberId = channel.PhoneNumberId,
                        BusinessAccountId = channel.BusinessAccountId,
                        DisplayPhoneNumber = channel.DisplayPhoneNumber,
                        AccessToken = channel.AccessToken,
                        WebhookVerifyToken = channel.WebhookVerifyToken,
                        AppSecret = channel.AppSecret,
                        IsActive = channel.IsActive,
                        IsVerified = channel.IsVerified,
                        AwayMessageEnabled = channel.AwayMessageEnabled,
                        AwayMessageText = channel.AwayMessageText
                    };

                if (setting is null)
                {
                    _logger.LogWarning("WhatsApp webhook ignored because phone number id {PhoneNumberId} is not configured.", phoneNumberId);
                    continue;
                }

                processedAny |= await ProcessInboundMessagesAsync(value, setting, channel?.Id, webhookEvent, createdMessages, awayMessageDispatches, cancellationToken);
                processedAny |= await ProcessStatusesAsync(value, webhookEvent, aiProcessingChanges, cancellationToken);
            }
        }

        return processedAny;
    }

    private async Task<bool> ProcessInboundMessagesAsync(
        JsonElement value,
        WhatsAppBranchSetting setting,
        int? channelSettingId,
        WhatsAppWebhookEvent webhookEvent,
        List<WhatsAppMessage> createdMessages,
        List<WhatsAppAwayMessageDispatch> awayMessageDispatches,
        CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return false;

        var processed = false;
        foreach (var messageElement in messages.EnumerateArray())
        {
            var messageId = TryGetString(messageElement, "id");
            var typeText = TryGetString(messageElement, "type");
            if (!TryMapMessageType(typeText, out var messageType))
                continue;

            if (!string.IsNullOrWhiteSpace(messageId)
                && await _db.WhatsAppMessages.AnyAsync(x => x.WhatsAppMessageId == messageId, cancellationToken))
            {
                continue;
            }

            var identity = WhatsAppWebhookIdentityReader.Read(value, messageElement);
            var mediaPayload = TryGetMediaPayload(messageElement, messageType);
            var text = messageType == WhatsAppMessageType.Text
                ? TryGetTextBody(messageElement)
                : TryGetMediaCaptionOrName(mediaPayload, messageType);
            if (identity.PhoneNumber is null && identity.UserId is null)
            {
                _logger.LogWarning("WhatsApp inbound message {MessageId} ignored because it has neither phone nor BSUID.", messageId);
                continue;
            }

            var timestamp = ParseWhatsAppTimestamp(TryGetString(messageElement, "timestamp")) ?? _clock.UtcNow;
            var conversationByUserId = identity.UserId is null
                ? null
                : _db.WhatsAppConversations.Local.FirstOrDefault(x =>
                    (channelSettingId.HasValue ? x.ChannelSettingId == channelSettingId : x.BranchId == setting.BranchId && x.ChannelSettingId == null)
                    && x.WhatsAppUserId == identity.UserId)
                    ?? await _db.WhatsAppConversations.FirstOrDefaultAsync(x =>
                        (channelSettingId.HasValue ? x.ChannelSettingId == channelSettingId : x.BranchId == setting.BranchId && x.ChannelSettingId == null)
                        && x.WhatsAppUserId == identity.UserId,
                        cancellationToken);
            var conversationByPhone = identity.PhoneNumber is null
                ? null
                : _db.WhatsAppConversations.Local.FirstOrDefault(x =>
                    (channelSettingId.HasValue ? x.ChannelSettingId == channelSettingId : x.BranchId == setting.BranchId && x.ChannelSettingId == null)
                    && x.PhoneNumber == identity.PhoneNumber)
                    ?? await _db.WhatsAppConversations.FirstOrDefaultAsync(x =>
                        (channelSettingId.HasValue ? x.ChannelSettingId == channelSettingId : x.BranchId == setting.BranchId && x.ChannelSettingId == null)
                        && x.PhoneNumber == identity.PhoneNumber,
                        cancellationToken);
            var phoneIdentityMismatch = conversationByUserId is null
                && conversationByPhone is not null
                && identity.UserId is not null
                && !string.IsNullOrWhiteSpace(conversationByPhone.WhatsAppUserId)
                && !string.Equals(conversationByPhone.WhatsAppUserId, identity.UserId, StringComparison.Ordinal);
            if (phoneIdentityMismatch)
            {
                _logger.LogWarning(
                    "WhatsApp identity changed in branch {BranchId}. A separate history is required.",
                    setting.BranchId);
            }
            var hasIdentityConflict = conversationByUserId is not null
                && conversationByPhone is not null
                && conversationByUserId.Id != conversationByPhone.Id;
            if (hasIdentityConflict)
            {
                _logger.LogWarning(
                    "WhatsApp identity conflict in branch {BranchId}: BSUID conversation {UserConversationId} and phone conversation {PhoneConversationId}. Histories were kept separate.",
                    setting.BranchId,
                    conversationByUserId!.Id,
                    conversationByPhone!.Id);
            }

            var conversation = conversationByUserId ?? (phoneIdentityMismatch ? null : conversationByPhone);
            if (conversation is null)
            {
                var aiActive = channelSettingId.HasValue
                    ? await _db.TenantAiSettings.AsNoTracking().AnyAsync(x => x.TenantId == TenantId && x.IsActive && x.IsVerified, cancellationToken)
                    : await IsBranchAiActiveAsync(setting.BranchId, cancellationToken);
                conversation = new WhatsAppConversation
                {
                    TenantId = TenantId,
                    ChannelSettingId = channelSettingId,
                    BranchId = setting.BranchId,
                    PhoneNumber = phoneIdentityMismatch ? null : identity.PhoneNumber,
                    WhatsAppUserId = identity.UserId,
                    WhatsAppUsername = identity.Username,
                    Status = WhatsAppConversationStatus.Open,
                    AttentionMode = _attentionService.InitialMode(aiActive),
                    AttentionModeUpdatedAt = timestamp
                };
                _db.WhatsAppConversations.Add(conversation);
            }

            Customer? customer = null;
            if (conversation.CustomerId.HasValue)
            {
                customer = await _db.Customers.FirstOrDefaultAsync(x =>
                    x.TenantId == TenantId && x.Id == conversation.CustomerId.Value && (!channelSettingId.HasValue || x.Active),
                    cancellationToken);
            }
            if (customer is null && identity.UserId is not null)
            {
                var matches = await _db.Customers.Where(x => x.TenantId == TenantId && x.Active && x.WhatsAppUserId == identity.UserId)
                    .OrderBy(x => x.Id).Take(2).ToListAsync(cancellationToken);
                customer = matches.Count == 1 ? matches[0] : null;
            }
            customer ??= identity.PhoneNumber is null || phoneIdentityMismatch
                ? null
                : await FindUniqueCustomerByPhoneAsync(identity.PhoneNumber, cancellationToken);

            if (channelSettingId.HasValue)
            {
                customer = identity.PhoneNumber is null || phoneIdentityMismatch || hasIdentityConflict
                    ? null : await FindUniqueCustomerByPhoneAsync(identity.PhoneNumber, cancellationToken);
                conversation.CustomerId = customer?.Id;
            }

            if (customer is not null)
            {
                conversation.CustomerId ??= customer.Id;
                if (identity.UserId is not null
                    && (string.IsNullOrWhiteSpace(customer.WhatsAppUserId)
                        || string.Equals(customer.WhatsAppUserId, identity.UserId, StringComparison.Ordinal)))
                {
                    customer.WhatsAppUserId = identity.UserId;
                    customer.WhatsAppUsername = identity.Username ?? customer.WhatsAppUsername;
                }
                if (!channelSettingId.HasValue && string.IsNullOrWhiteSpace(customer.Phone1) && identity.PhoneNumber is not null)
                {
                    var customerPhone = NormalizeCustomerPhone(identity.PhoneNumber);
                    if (customerPhone is not null && await CanAssignPhoneToCustomerAsync(customer.Id, customerPhone, cancellationToken))
                    {
                        customer.Phone1 = customerPhone;
                        customer.Phones.Add(new CustomerPhone
                        {
                            TenantId = customer.TenantId,
                            PhoneNormalized = customerPhone,
                            IsPrimary = true
                        });
                    }
                }
            }

            if (!hasIdentityConflict && !phoneIdentityMismatch)
            {
                conversation.PhoneNumber = identity.PhoneNumber ?? conversation.PhoneNumber;
                conversation.WhatsAppUserId = identity.UserId ?? conversation.WhatsAppUserId;
            }
            conversation.WhatsAppUsername = identity.Username ?? conversation.WhatsAppUsername;
            conversation.ContactName = identity.ContactName ?? conversation.ContactName;

            if (hasIdentityConflict && customer is not null)
            {
                if (!conversationByUserId!.CustomerId.HasValue)
                    conversationByUserId.CustomerId = customer.Id;
                if (!conversationByPhone!.CustomerId.HasValue)
                    conversationByPhone.CustomerId = customer.Id;
            }
            var completedFlowToken = TryGetNfmReplyFlowToken(messageElement);
            if (channelSettingId.HasValue && completedFlowToken is not null)
            {
                var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(completedFlowToken))).ToLowerInvariant();
                var completedSession = await _db.WhatsAppCommerceSessions.FirstOrDefaultAsync(
                    x => x.ChannelSettingId == channelSettingId.Value
                        && x.ConversationId == conversation.Id
                        && x.FlowTokenHash == tokenHash,
                    cancellationToken);
                if (completedSession is not null && completedSession.Status == "active")
                {
                    completedSession.Status = "completed";
                    completedSession.CompletedAt = timestamp;
                }
            }

            if (conversation.Id == 0 && messageType != WhatsAppMessageType.Text && mediaPayload is not null)
                await _db.SaveChangesAsync(cancellationToken);

            var inboundMedia = messageType == WhatsAppMessageType.Text || mediaPayload is null
                ? null
                : await DownloadAndStoreInboundMediaAsync(setting, conversation.BranchId, conversation.Id, mediaPayload.Value, messageType, cancellationToken);

            conversation.LastMessageAt = timestamp;
            conversation.LastMessagePreview = PreviewForMedia(messageType, text ?? string.Empty);
            conversation.UnreadCount += 1;

            var aiProcessingStatus = WhatsAppAiProcessingStatus.Pending;
            DateTime? aiProcessedAt = null;
            string? aiProcessingError = null;
            if (!webhookEvent.AttentionVersion.HasValue && webhookEvent.ProcessingState != "legacy"
                && conversation.AttentionModeUpdatedAt > webhookEvent.CreatedAt)
            {
                aiProcessingStatus = WhatsAppAiProcessingStatus.Ignored;
                aiProcessedAt = _clock.UtcNow;
                aiProcessingError = "attention_changed_before_ingress_processing";
            }
            if (webhookEvent.ConversationId.HasValue && webhookEvent.ConversationId != conversation.Id)
            {
                aiProcessingStatus = WhatsAppAiProcessingStatus.Ignored;
                aiProcessedAt = _clock.UtcNow;
                aiProcessingError = "identity_changed_after_ingress";
            }
            if (completedFlowToken is not null)
            {
                aiProcessingStatus = WhatsAppAiProcessingStatus.Ignored;
                aiProcessedAt = _clock.UtcNow;
                aiProcessingError = "whatsapp_flow_completed";
            }
            DateTime? awayClosedPeriodStartedAtUtc = null;
            string? awayText = null;
            if (!channelSettingId.HasValue && setting.AwayMessageEnabled)
            {
                var receivedAtUtc = _clock.UtcNow;
                var evaluation = await _businessHoursService.Evaluate(setting.BranchId, receivedAtUtc, cancellationToken);
                if (!evaluation.IsConfigured)
                {
                    _logger.LogWarning(
                        "WhatsApp away message skipped because branch business hours are missing or invalid. BranchId={BranchId}",
                        setting.BranchId);
                }
                else if (!evaluation.IsOpen
                    && evaluation.ClosedPeriodStartedAtUtc.HasValue
                    && evaluation.NextOpeningAtUtc.HasValue)
                {
                    var template = setting.AwayMessageText;
                    var validationError = _awayMessageService.ValidateTemplate(template);
                    if (validationError is not null)
                    {
                        _logger.LogWarning(
                            "WhatsApp away message skipped because its template is invalid. BranchId={BranchId} Error={Error}",
                            setting.BranchId,
                            validationError);
                    }
                    else
                    {
                        var branchName = await _db.Branches.AsNoTracking()
                            .Where(x => x.Id == setting.BranchId)
                            .Select(x => x.Name)
                            .FirstAsync(cancellationToken);
                        var rendered = _awayMessageService.Render(
                            template!,
                            branchName,
                            receivedAtUtc,
                            evaluation.NextOpeningAtUtc.Value);
                        aiProcessingStatus = WhatsAppAiProcessingStatus.Ignored;
                        aiProcessedAt = receivedAtUtc;
                        aiProcessingError = "outside_business_hours";
                        awayClosedPeriodStartedAtUtc = evaluation.ClosedPeriodStartedAtUtc.Value;
                        awayText = rendered;
                    }
                }
            }

            var message = new WhatsAppMessage
            {
                Conversation = conversation,
                WhatsAppMessageId = messageId,
                Direction = WhatsAppMessageDirection.Inbound,
                Type = messageType,
                TextBody = text ?? string.Empty,
                MediaId = inboundMedia?.MediaId ?? mediaPayload?.MediaId,
                MediaUrl = inboundMedia?.MediaUrl,
                MediaMimeType = inboundMedia?.MimeType ?? mediaPayload?.MimeType,
                MediaFileName = inboundMedia?.FileName ?? mediaPayload?.FileName,
                MediaFileSize = inboundMedia?.FileSize,
                MediaSha256 = inboundMedia?.Sha256 ?? mediaPayload?.Sha256,
                Status = WhatsAppMessageStatus.Received,
                Timestamp = timestamp,
                RawPayload = SenorArroz.Application.Common.Helpers.WhatsAppFlowPayload.WithoutTokens(messageElement.GetRawText()),
                AiProcessingStatus = aiProcessingStatus,
                AiResponseAttentionVersion = webhookEvent.AttentionVersion ?? conversation.AttentionModeUpdatedAt,
                AiProcessedAt = aiProcessedAt,
                AiProcessingError = aiProcessingError
            };
            _db.WhatsAppMessages.Add(message);
            createdMessages.Add(message);
            if (awayClosedPeriodStartedAtUtc.HasValue && awayText is not null)
                awayMessageDispatches.Add(new WhatsAppAwayMessageDispatch(
                    message,
                    awayClosedPeriodStartedAtUtc.Value,
                    awayText));

            webhookEvent.EventType = "message";
            webhookEvent.WhatsAppMessageId ??= messageId;
            processed = true;
        }

        return processed;
    }

    private async Task<bool> ProcessStatusesAsync(
        JsonElement value,
        WhatsAppWebhookEvent webhookEvent,
        List<WhatsAppMessage> aiProcessingChanges,
        CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
            return false;

        var processed = false;
        foreach (var statusElement in statuses.EnumerateArray())
        {
            var messageId = TryGetString(statusElement, "id");
            var statusText = TryGetString(statusElement, "status");
            if (string.IsNullOrWhiteSpace(messageId) || !TryMapMessageStatus(statusText, out var status))
                continue;

            var message = await _db.WhatsAppMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.WhatsAppMessageId == messageId, cancellationToken);
            if (message is null)
                continue;

            var transition = await TryApplyOutboundStatusAsync(message.Id, status, cancellationToken);
            if (!transition.Applied)
            {
                webhookEvent.EventType = "status";
                webhookEvent.WhatsAppMessageId ??= messageId;
                processed = true;
                continue;
            }

            var previousStatus = transition.PreviousStatus;
            if (message.SentByAi)
            {
                var source = await FindAiSourceForOutboundAsync(message, cancellationToken);
                if (source is not null)
                {
                    if (status == WhatsAppMessageStatus.Failed)
                    {
                        source = await PersistMetaDeliveryFailureAsync(
                            source,
                            message.Id,
                            statusElement,
                            cancellationToken);
                        if (source is not null)
                        {
                            aiProcessingChanges.Add(source);
                            _logger.LogError(
                                "WhatsApp AI delivery failed ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} OutboundMessageId={OutboundMessageId} MetaError={MetaError}",
                                source.ConversationId,
                                source.Id,
                                message.Id,
                                source.AiProcessingError);
                        }
                    }
                    else if (previousStatus == WhatsAppMessageStatus.Failed
                             && WhatsAppMessageStatusTransitions.IsDeliveryProof(status)
                             && await TryPersistMetaDeliveryRecoveryAsync(
                                 source,
                                 message.Id,
                                 status,
                                 cancellationToken) is { } healedSource)
                    {
                        aiProcessingChanges.Add(healedSource);
                        _logger.LogInformation(
                            "WhatsApp AI delivery recovered ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} OutboundMessageId={OutboundMessageId} DeliveryStatus={DeliveryStatus}",
                            healedSource.ConversationId,
                            healedSource.Id,
                            message.Id,
                            status);
                    }
                }
            }
            webhookEvent.EventType = "status";
            webhookEvent.WhatsAppMessageId ??= messageId;
            processed = true;
        }

        return processed;
    }

    private async Task<(bool Applied, WhatsAppMessageStatus PreviousStatus)> TryApplyOutboundStatusAsync(
        int messageId,
        WhatsAppMessageStatus incoming,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            var tracked = await _db.WhatsAppMessages.FirstAsync(x => x.Id == messageId, cancellationToken);
            var previous = tracked.Status;
            if (!WhatsAppMessageStatusTransitions.ShouldApply(previous, incoming))
                return (false, previous);
            tracked.Status = incoming;
            return (true, previous);
        }

        // Compare-and-swap avoids two out-of-order webhook requests both
        // deciding from the same stale state. Retry after a lost race so a
        // delivery proof can heal Failed and Failed can never regress delivery.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var previous = await _db.WhatsAppMessages
                .AsNoTracking()
                .Where(x => x.Id == messageId)
                .Select(x => x.Status)
                .FirstAsync(cancellationToken);
            if (!WhatsAppMessageStatusTransitions.ShouldApply(previous, incoming))
                return (false, previous);

            var updated = await _db.WhatsAppMessages
                .Where(x => x.Id == messageId && x.Status == previous)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Status, incoming),
                    cancellationToken);
            if (updated == 1)
                return (true, previous);
        }

        var current = await _db.WhatsAppMessages
            .AsNoTracking()
            .Where(x => x.Id == messageId)
            .Select(x => x.Status)
            .FirstAsync(cancellationToken);
        return (false, current);
    }

    private async Task<WhatsAppMessage?> FindAiSourceForOutboundAsync(
        WhatsAppMessage outbound,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(outbound.WhatsAppMessageId))
        {
            var exact = await _db.WhatsAppMessages.FirstOrDefaultAsync(
                x => x.ConversationId == outbound.ConversationId
                    && x.Direction == WhatsAppMessageDirection.Inbound
                    && x.AiResponseWhatsAppMessageId == outbound.WhatsAppMessageId,
                cancellationToken);
            if (exact is not null)
                return exact;
        }

        if (!TryReadAutomaticAiAttemptId(outbound.RawPayload, out var attemptId))
            return null;

        var candidates = await _db.WhatsAppMessages
            .Where(x => x.ConversationId == outbound.ConversationId
                && x.Direction == WhatsAppMessageDirection.Inbound
                && x.AiResponseAttemptId == attemptId)
            .OrderByDescending(x => x.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
        {
            _logger.LogWarning(
                "WhatsApp AI delivery status was not correlated because AttemptId={AttemptId} is ambiguous ConversationId={ConversationId} OutboundMessageId={OutboundMessageId}",
                attemptId,
                outbound.ConversationId,
                outbound.Id);
        }

        return null;
    }

    private async Task<WhatsAppMessage?> PersistMetaDeliveryFailureAsync(
        WhatsAppMessage source,
        int outboundMessageId,
        JsonElement statusElement,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()
            || source.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman)
        {
            var outboundStillFailed = await _db.WhatsAppMessages
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == outboundMessageId && x.Status == WhatsAppMessageStatus.Failed,
                    cancellationToken);
            if (!outboundStillFailed)
                return null;
            ApplyMetaDeliveryFailure(source, statusElement);
            return source;
        }

        var metaError = ExtractMetaDeliveryError(statusElement);
        var storedError = metaError[..Math.Min(1000, metaError.Length)];
        var now = _clock.UtcNow;
        var updated = await _db.WhatsAppMessages
            .Where(x =>
                x.Id == source.Id
                && x.AiProcessingStatus != WhatsAppAiProcessingStatus.TransferredToHuman
                && _db.WhatsAppMessages.Any(outbound =>
                    outbound.Id == outboundMessageId
                    && outbound.Status == WhatsAppMessageStatus.Failed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.AiProcessingStatus, WhatsAppAiProcessingStatus.Failed)
                    .SetProperty(x => x.AiProcessingError, storedError)
                    .SetProperty(x => x.AiProcessingStartedAt, (DateTime?)null)
                    .SetProperty(x => x.AiNextRetryAt, (DateTime?)null)
                    .SetProperty(x => x.AiProcessedAt, now),
                cancellationToken);
        return updated == 1
            ? await _db.WhatsAppMessages.AsNoTracking().FirstAsync(x => x.Id == source.Id, cancellationToken)
            : null;
    }

    private async Task<WhatsAppMessage?> TryPersistMetaDeliveryRecoveryAsync(
        WhatsAppMessage source,
        int outboundMessageId,
        WhatsAppMessageStatus deliveryStatus,
        CancellationToken cancellationToken)
    {
        if (!WhatsAppMessageStatusTransitions.IsDeliveryProof(deliveryStatus))
            return null;

        if (!_db.Database.IsRelational()
            || source.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman)
        {
            var outboundIsDelivered = await _db.WhatsAppMessages
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == outboundMessageId
                        && (x.Status == WhatsAppMessageStatus.Delivered
                            || x.Status == WhatsAppMessageStatus.Read),
                    cancellationToken);
            return outboundIsDelivered && TryHealMetaDeliveryFailure(source) ? source : null;
        }

        const string metaFailurePrefix = "Meta reportó que la respuesta de IA no pudo entregarse.";
        var now = _clock.UtcNow;
        var updated = await _db.WhatsAppMessages
            .Where(x =>
                x.Id == source.Id
                && x.AiProcessingStatus == WhatsAppAiProcessingStatus.Failed
                && x.AiProcessingError != null
                && x.AiProcessingError.StartsWith(metaFailurePrefix)
                && _db.WhatsAppMessages.Any(outbound =>
                    outbound.Id == outboundMessageId
                    && (outbound.Status == WhatsAppMessageStatus.Delivered
                        || outbound.Status == WhatsAppMessageStatus.Read)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.AiProcessingStatus, WhatsAppAiProcessingStatus.Completed)
                    .SetProperty(x => x.AiProcessingError, (string?)null)
                    .SetProperty(x => x.AiProcessingStartedAt, (DateTime?)null)
                    .SetProperty(x => x.AiNextRetryAt, (DateTime?)null)
                    .SetProperty(x => x.AiProcessedAt, now),
                cancellationToken);
        return updated == 1
            ? await _db.WhatsAppMessages.AsNoTracking().FirstAsync(x => x.Id == source.Id, cancellationToken)
            : null;
    }

    private void ApplyMetaDeliveryFailure(WhatsAppMessage source, JsonElement statusElement)
    {
        const string transferMarker = " | Aviso al cliente no entregado: ";
        var metaError = ExtractMetaDeliveryError(statusElement);
        if (source.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman)
        {
            var currentReason = source.AiProcessingError ?? "La conversación requiere atención humana.";
            var markerAt = currentReason.IndexOf(transferMarker, StringComparison.Ordinal);
            if (markerAt >= 0)
                currentReason = currentReason[..markerAt];
            var combined = $"{currentReason}{transferMarker}{metaError}";
            source.AiProcessingError = combined[..Math.Min(1000, combined.Length)];
        }
        else
        {
            source.AiProcessingStatus = WhatsAppAiProcessingStatus.Failed;
            source.AiProcessingError = metaError[..Math.Min(1000, metaError.Length)];
        }

        source.AiProcessingStartedAt = null;
        source.AiNextRetryAt = null;
        source.AiProcessedAt = _clock.UtcNow;
    }

    private bool TryHealMetaDeliveryFailure(WhatsAppMessage source)
    {
        const string metaFailurePrefix = "Meta reportó que la respuesta de IA no pudo entregarse.";
        const string transferMarker = " | Aviso al cliente no entregado: ";

        if (source.AiProcessingStatus == WhatsAppAiProcessingStatus.Failed
            && source.AiProcessingError?.StartsWith(metaFailurePrefix, StringComparison.Ordinal) == true)
        {
            source.AiProcessingStatus = WhatsAppAiProcessingStatus.Completed;
            source.AiProcessingError = null;
            source.AiProcessedAt = _clock.UtcNow;
            return true;
        }

        if (source.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman
            && source.AiProcessingError is { } transferError)
        {
            var markerAt = transferError.IndexOf(transferMarker, StringComparison.Ordinal);
            if (markerAt >= 0)
            {
                source.AiProcessingError = transferError[..markerAt];
                source.AiProcessedAt = _clock.UtcNow;
                return true;
            }
        }

        return false;
    }

    private async Task<Customer?> FindUniqueCustomerByPhoneAsync(string whatsappPhone, CancellationToken cancellationToken)
    {
        var digits = OnlyDigits(whatsappPhone);
        if (!ColombianPhoneNormalizer.TryNormalize(whatsappPhone, out var last10))
            return null;
        var matches = await _db.Customers
            .Where(x => x.TenantId == TenantId && x.Active
                && (x.Phones.Any(p => p.Active && p.PhoneNormalized == last10)
                    || x.Phone1 == digits || x.Phone2 == digits || x.Phone1 == last10 || x.Phone2 == last10))
            .OrderBy(x => x.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    internal static string? TryGetPhoneNumberId(JsonElement value)
    {
        return value.TryGetProperty("metadata", out var metadata)
            ? TryGetString(metadata, "phone_number_id")
            : null;
    }

    private static string? TryGetTextBody(JsonElement message)
    {
        if(message.TryGetProperty("text",out var text))return TryGetString(text,"body");
        if(message.TryGetProperty("interactive",out var interactive)&&interactive.TryGetProperty("button_reply",out var reply))
        {
            var id=TryGetString(reply,"id");
            if(id?.StartsWith("address:",StringComparison.OrdinalIgnoreCase)==true)return $"Seleccionar dirección {id[8..]}";
            return TryGetString(reply,"title");
        }
        if (message.TryGetProperty("interactive", out interactive)
            && interactive.TryGetProperty("nfm_reply", out var flowReply))
            return TryGetString(flowReply, "body") ?? "Pedido completado en WhatsApp Flow";
        return null;
    }

    private static string? TryGetNfmReplyFlowToken(JsonElement message)
    {
        if (!message.TryGetProperty("interactive", out var interactive)
            || !interactive.TryGetProperty("nfm_reply", out var reply))
            return null;
        var responseJson = TryGetString(reply, "response_json");
        if (string.IsNullOrWhiteSpace(responseJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            return TryGetString(document.RootElement, "flow_token");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static InboundMediaPayload? TryGetMediaPayload(JsonElement message, WhatsAppMessageType messageType)
    {
        var property = MediaTypeToApi(messageType);
        if (property == "text" || !message.TryGetProperty(property, out var media) || media.ValueKind != JsonValueKind.Object)
            return null;

        return new InboundMediaPayload(
            TryGetString(media, "id") ?? string.Empty,
            TryGetString(media, "mime_type"),
            TryGetString(media, "sha256"),
            TryGetString(media, "filename"),
            TryGetString(media, "caption"));
    }

    private async Task<StoredWhatsAppMedia?> DownloadAndStoreInboundMediaAsync(
        WhatsAppBranchSetting setting,
        int branchId,
        int conversationId,
        InboundMediaPayload mediaPayload,
        WhatsAppMessageType messageType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaPayload.MediaId))
            return null;

        var info = await _whatsAppCloudClient.GetMediaInfoAsync(mediaPayload.MediaId, setting.AccessToken, cancellationToken);
        if (!info.Success || string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            _logger.LogWarning("Could not get WhatsApp media info for media id {MediaId}: {Error}", mediaPayload.MediaId, info.ErrorMessage);
            return new StoredWhatsAppMedia(mediaPayload.MediaId, null, mediaPayload.MimeType, mediaPayload.FileName, null, mediaPayload.Sha256);
        }

        var download = await _whatsAppCloudClient.DownloadMediaAsync(info.DownloadUrl, setting.AccessToken, cancellationToken);
        if (!download.Success || download.Content is null)
        {
            _logger.LogWarning("Could not download WhatsApp media id {MediaId}: {Error}", mediaPayload.MediaId, download.ErrorMessage);
            return new StoredWhatsAppMedia(mediaPayload.MediaId, null, info.MimeType ?? mediaPayload.MimeType, mediaPayload.FileName, info.FileSize, info.Sha256 ?? mediaPayload.Sha256);
        }

        var mimeType = download.ContentType ?? info.MimeType ?? mediaPayload.MimeType ?? "application/octet-stream";
        var fileName = string.IsNullOrWhiteSpace(mediaPayload.FileName)
            ? $"{mediaPayload.MediaId}{ExtensionFromContentType(mimeType, messageType)}"
            : SanitizeFileName(mediaPayload.FileName);
        string? storageUrl = null;
        try
        {
            storageUrl = await SaveWhatsAppMediaToFirebaseAsync(branchId, conversationId, download.Content, fileName, mimeType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not upload inbound WhatsApp media id {MediaId} to Firebase.", mediaPayload.MediaId);
        }

        return new StoredWhatsAppMedia(
            mediaPayload.MediaId,
            storageUrl,
            mimeType,
            fileName,
            info.FileSize ?? download.Content.LongLength,
            info.Sha256 ?? mediaPayload.Sha256);
    }

    internal async Task<string> SaveWhatsAppMediaToFirebaseAsync(
        int branchId,
        int conversationId,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrWhiteSpace(_firebaseOptions.WhatsAppMediaPrefix)
            ? "whatsapp-media"
            : _firebaseOptions.WhatsAppMediaPrefix.Trim().TrimStart('/').TrimEnd('/');
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ExtensionFromContentType(contentType, MediaTypeFromContentType(contentType, fileName));
        var objectName = $"tenants/{TenantId}/{prefix}/{branchId}/{conversationId}/{_clock.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{ext}";
        return await _firebaseStorage.UploadPublicObjectAsync(content, objectName, contentType, cancellationToken);
    }

    internal static string? NormalizeCustomerPhone(string? value)
    {
        return ColombianPhoneNormalizer.TryNormalize(value, out var normalized) ? normalized : null;
    }

    internal async Task<bool> CanAssignPhoneToCustomerAsync(
        int customerId,
        string phone,
        CancellationToken cancellationToken) =>
        !await _db.Customers.AsNoTracking().AnyAsync(x =>
            x.TenantId == TenantId
            && x.Id != customerId
            && (x.Phones.Any(p => p.Active && p.PhoneNormalized == phone) || x.Phone1 == phone || x.Phone2 == phone),
            cancellationToken);

    internal static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryReadAutomaticAiAttemptId(string? rawPayload, out string attemptId)
    {
        attemptId = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPayload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            if (!root.TryGetProperty("origin", out var origin)
                || origin.GetString() is not ("ai" or "ai_transfer")
                || !root.TryGetProperty("attemptId", out var attempt))
                return false;

            attemptId = attempt.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(attemptId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractMetaDeliveryError(JsonElement statusElement)
    {
        const string fallback = "Meta reportó que la respuesta de IA no pudo entregarse.";
        if (!statusElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            return fallback;

        var details = new List<string>();
        foreach (var error in errors.EnumerateArray())
        {
            if (error.TryGetProperty("code", out var code)
                && code.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                details.Add($"código {code.ToString()}");
            AddMetaErrorPart(details, TryGetString(error, "title"));
            AddMetaErrorPart(details, TryGetString(error, "message"));
            if (error.TryGetProperty("error_data", out var errorData) && errorData.ValueKind == JsonValueKind.Object)
                AddMetaErrorPart(details, TryGetString(errorData, "details"));
        }

        return details.Count == 0
            ? fallback
            : $"{fallback} {string.Join(" · ", details.Distinct(StringComparer.OrdinalIgnoreCase))}";
    }

    private static void AddMetaErrorPart(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add(value.Trim());
    }

    private static DateTime? ParseWhatsAppTimestamp(string? timestamp)
    {
        if (!long.TryParse(timestamp, out var seconds))
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    internal static string OnlyDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static bool TryMapMessageStatus(string? value, out WhatsAppMessageStatus status)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "sent":
                status = WhatsAppMessageStatus.Sent;
                return true;
            case "delivered":
                status = WhatsAppMessageStatus.Delivered;
                return true;
            case "read":
                status = WhatsAppMessageStatus.Read;
                return true;
            case "failed":
                status = WhatsAppMessageStatus.Failed;
                return true;
            default:
                status = WhatsAppMessageStatus.Received;
                return false;
        }
    }

    private static bool TryMapMessageType(string? value, out WhatsAppMessageType type)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "text":
                type = WhatsAppMessageType.Text;
                return true;
            case "interactive":
                type = WhatsAppMessageType.Text;
                return true;
            case "image":
                type = WhatsAppMessageType.Image;
                return true;
            case "audio":
                type = WhatsAppMessageType.Audio;
                return true;
            case "video":
                type = WhatsAppMessageType.Video;
                return true;
            case "document":
                type = WhatsAppMessageType.Document;
                return true;
            case "sticker":
                type = WhatsAppMessageType.Sticker;
                return true;
            default:
                type = WhatsAppMessageType.Text;
                return false;
        }
    }

    internal Task<bool> IsBranchAiActiveAsync(int branchId, CancellationToken ct) => _db.BranchAiSettings.AsNoTracking().AnyAsync(x => x.BranchId == branchId && x.IsActive && x.IsVerified, ct);

    internal static WhatsAppConversationDto ToConversationDto(WhatsAppConversation conversation, string? assignedUserName = null, string? attentionReason = null)
        => SenorArroz.Application.Common.Helpers.WhatsAppConversationMapper.ToDto(conversation, assignedUserName, attentionReason);

    internal WhatsAppMessageDto ToMessageDto(WhatsAppMessage message) => new()
    {
        Id = message.Id,
        ConversationId = message.ConversationId,
        WhatsAppMessageId = message.WhatsAppMessageId,
        Direction = message.Direction == WhatsAppMessageDirection.Inbound ? "inbound" : "outbound",
        Type = MediaTypeToApi(message.Type),
        TextBody = message.TextBody,
        MediaId = message.MediaId,
        MediaUrl = message.MediaUrl,
        MediaMimeType = message.MediaMimeType,
        MediaFileName = message.MediaFileName,
        MediaFileSize = message.MediaFileSize,
        MediaSha256 = message.MediaSha256,
        Status = MessageStatusToApi(message.Status),
        SentByUserId = message.SentByUserId,
        Timestamp = AsUtc(message.Timestamp),
        CreatedAt = AsUtc(message.CreatedAt),
        AiProcessing = message.Direction == WhatsAppMessageDirection.Inbound
            && message.AiProcessingStatus != WhatsAppAiProcessingStatus.NotApplicable
                ? WhatsAppAiDiagnosticsMapper.ToDto(message, _aiMaxPersistentAttempts, includeTechnicalDetail: false)
                : null
    };

    // PostgreSQL `timestamp without time zone` values are materialized with
    // DateTimeKind.Unspecified. They contain UTC in this application, so mark
    // them explicitly before JSON serialization (which then emits the `Z`).
    internal static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    internal static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private static string MessageStatusToApi(WhatsAppMessageStatus status) => status switch
    {
        WhatsAppMessageStatus.Received => "received",
        WhatsAppMessageStatus.Sent => "sent",
        WhatsAppMessageStatus.Delivered => "delivered",
        WhatsAppMessageStatus.Read => "read",
        WhatsAppMessageStatus.Failed => "failed",
        _ => "received"
    };

    internal static string MediaTypeToApi(WhatsAppMessageType type) => type switch
    {
        WhatsAppMessageType.Text => "text",
        WhatsAppMessageType.Image => "image",
        WhatsAppMessageType.Audio => "audio",
        WhatsAppMessageType.Video => "video",
        WhatsAppMessageType.Document => "document",
        WhatsAppMessageType.Sticker => "sticker",
        _ => "document"
    };

    internal static WhatsAppMessageType MediaTypeFromContentType(string? contentType, string? fileName)
    {
        var ct = (contentType ?? string.Empty).ToLowerInvariant();
        if (ct.StartsWith("image/", StringComparison.Ordinal))
            return WhatsAppMessageType.Image;
        if (ct.StartsWith("audio/", StringComparison.Ordinal))
            return WhatsAppMessageType.Audio;
        if (ct.StartsWith("video/", StringComparison.Ordinal))
            return WhatsAppMessageType.Video;

        var name = (fileName ?? string.Empty).ToLowerInvariant();
        if (name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".png") || name.EndsWith(".webp") || name.EndsWith(".gif"))
            return WhatsAppMessageType.Image;
        if (name.EndsWith(".mp3") || name.EndsWith(".ogg") || name.EndsWith(".opus") || name.EndsWith(".m4a") || name.EndsWith(".wav"))
            return WhatsAppMessageType.Audio;
        if (name.EndsWith(".mp4") || name.EndsWith(".3gp") || name.EndsWith(".mov"))
            return WhatsAppMessageType.Video;
        return WhatsAppMessageType.Document;
    }

    internal static string PreviewForMedia(WhatsAppMessageType type, string text)
    {
        var label = type switch
        {
            WhatsAppMessageType.Image => "Imagen",
            WhatsAppMessageType.Audio => "Audio",
            WhatsAppMessageType.Video => "Video",
            WhatsAppMessageType.Document => "Documento",
            WhatsAppMessageType.Sticker => "Sticker",
            _ => text
        };
        var trimmed = text.Trim();
        var preview = type == WhatsAppMessageType.Text ? trimmed : string.IsNullOrWhiteSpace(trimmed) ? label : $"{label}: {trimmed}";
        return preview.Length > 500 ? preview[..500] : preview;
    }

    private static string? TryGetMediaCaptionOrName(InboundMediaPayload? media, WhatsAppMessageType type)
    {
        if (media is null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(media.Value.Caption))
            return media.Value.Caption;
        if (type == WhatsAppMessageType.Document && !string.IsNullOrWhiteSpace(media.Value.FileName))
            return media.Value.FileName;
        return string.Empty;
    }

    internal static string SanitizeFileName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "archivo" : Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "archivo" : name;
    }

    private static string ExtensionFromContentType(string contentType, WhatsAppMessageType type)
    {
        var ct = (contentType ?? string.Empty).ToLowerInvariant();
        return ct switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "audio/mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/mp4" => ".m4a",
            "audio/aac" => ".aac",
            "video/mp4" => ".mp4",
            "video/3gpp" => ".3gp",
            "application/pdf" => ".pdf",
            _ => type switch
            {
                WhatsAppMessageType.Image => ".jpg",
                WhatsAppMessageType.Audio => ".ogg",
                WhatsAppMessageType.Video => ".mp4",
                WhatsAppMessageType.Document => ".bin",
                WhatsAppMessageType.Sticker => ".webp",
                _ => ".bin"
            }
        };
    }

    internal readonly record struct InboundMediaPayload(
        string MediaId,
        string? MimeType,
        string? Sha256,
        string? FileName,
        string? Caption);

    internal readonly record struct StoredWhatsAppMedia(
        string MediaId,
        string? MediaUrl,
        string? MimeType,
        string? FileName,
        long? FileSize,
        string? Sha256);

}
