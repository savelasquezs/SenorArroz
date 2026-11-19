using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;

namespace SenorArroz.API.Services;

public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IHubContext<WhatsAppHub> _hubContext;
    private readonly ILogger<WhatsAppNotificationService> _logger;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly WhatsAppProcessingOptions _processing;

    public WhatsAppNotificationService(
        IHubContext<WhatsAppHub> hubContext,
        IApplicationDbContext db,
        ICurrentTenant tenant,
        ILogger<WhatsAppNotificationService> logger,
        IOptions<WhatsAppProcessingOptions>? processing = null)
    {
        _hubContext = hubContext;
        _db = db;
        _tenant = tenant;
        _logger = logger;
        _processing = processing?.Value ?? new();
    }

    public async Task NotifyMessageCreatedAsync(
        int branchId,
        WhatsAppConversationDto conversation,
        WhatsAppMessageDto message,
        CancellationToken cancellationToken = default)
    {
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var payload = new
        {
            branchId = conversation.OperationalBranchId ?? branchId,
            conversation,
            message
        };

        await PublishAsync("WhatsAppMessageCreated", conversation.Id, payload,
            WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, branchId, conversation), cancellationToken);

        _logger.LogInformation(
            "WhatsApp SignalR message emitted. BranchId={BranchId} ConversationId={ConversationId} MessageId={MessageId}",
            branchId,
            conversation.Id,
            message.Id);
    }

    public async Task NotifyAttentionChangedAsync(int branchId, WhatsAppConversationDto conversation, CancellationToken cancellationToken = default)
    {
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var payload = new { branchId = conversation.OperationalBranchId ?? branchId, conversation };
        await PublishAsync("WhatsAppAttentionChanged", conversation.Id, payload,
            WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, branchId, conversation), cancellationToken);
    }

    public Task NotifyInboxScopeChangedAsync(int branchId, CancellationToken cancellationToken = default) =>
        PublishAsync(WhatsAppRealtimeGroupResolver.InboxChanged, 0, new { branchId },
            WhatsAppRealtimeGroupResolver.InboxScope(_tenant.TenantId, branchId), cancellationToken);

    public Task NotifyInboxLabelCatalogChangedAsync(CancellationToken cancellationToken = default) =>
        PublishAsync("WhatsAppInboxLabelCatalogChanged", 0, new { },
            WhatsAppRealtimeGroupResolver.InboxScope(_tenant.TenantId, 0), cancellationToken);

    public async Task NotifyInboxConversationChangedAsync(int conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().Where(x => x.Id == conversationId)
            .Select(x => new { x.BranchId, Dto = new WhatsAppConversationDto
            {
                Id = x.Id, IsCentralChannel = x.ChannelSettingId != null, OperationalBranchId = x.OperationalBranchId, AssignedUserId = x.AssignedUserId
            } }).SingleOrDefaultAsync(cancellationToken);
        if (conversation is null) return;
        await PublishAsync(WhatsAppRealtimeGroupResolver.InboxChanged, conversationId, new { conversationId },
            WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, conversation.BranchId, conversation.Dto), cancellationToken);
    }

    public async Task NotifyConversationRoutingChangedAsync(
        int branchId,
        int? previousOperationalBranchId,
        int? previousAssignedUserId,
        WhatsAppConversationDto conversation,
        CancellationToken cancellationToken = default)
    {
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var previous = new WhatsAppConversationDto
        {
            IsCentralChannel = conversation.IsCentralChannel,
            OperationalBranchId = previousOperationalBranchId,
            AssignedUserId = previousAssignedUserId
        };
        var groups = WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, branchId, previous)
            .Concat(WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, branchId, conversation))
            .Distinct()
            .ToArray();
        var payload = new
        {
            branchId = conversation.OperationalBranchId ?? branchId,
            previousOperationalBranchId,
            conversation
        };

        await PublishAsync("WhatsAppConversationRoutingChanged", conversation.Id, payload, groups, cancellationToken);
    }

    public async Task NotifyAiProcessingChangedAsync(
        int branchId,
        WhatsAppAiProcessingDto processing,
        CancellationToken cancellationToken = default)
    {
        // The branch group also contains cashiers. Keep provider bodies out of the
        // broadcast; authorized admins can retrieve the sanitized detail via REST.
        var realtimeProcessing = new WhatsAppAiProcessingDto
        {
            MessageId = processing.MessageId,
            ConversationId = processing.ConversationId,
            Status = processing.Status,
            Severity = processing.Severity,
            Title = processing.Title,
            Detail = processing.Detail,
            ErrorCategory = processing.ErrorCategory,
            HttpStatusCode = processing.HttpStatusCode,
            Attempts = processing.Attempts,
            MaxAttempts = processing.MaxAttempts,
            WillRetry = processing.WillRetry,
            Timestamp = processing.Timestamp,
            StatusChangedAt = processing.StatusChangedAt,
            StartedAt = processing.StartedAt,
            NextRetryAt = processing.NextRetryAt,
            ProcessedAt = processing.ProcessedAt
        };
        var conversation = new WhatsAppConversationDto { Id = processing.ConversationId };
        await ResolveCentralRoutingAsync(conversation, cancellationToken);
        var payload = new { branchId = conversation.OperationalBranchId ?? branchId, processing = realtimeProcessing };
        await PublishAsync("WhatsAppAiProcessingChanged", conversation.Id, payload,
            WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, branchId, conversation), cancellationToken);
        _logger.LogInformation(
            "WhatsApp AI processing update emitted. BranchId={BranchId} ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Status={Status} Attempts={Attempts}",
            branchId,
            processing.ConversationId,
            processing.MessageId,
            processing.Status,
            processing.Attempts);
    }

    private async Task PublishAsync(string name, int conversationId, object payload,
        IReadOnlyList<string> groups, CancellationToken ct)
    {
        if (!_processing.External)
        {
            if (name == "WhatsAppConversationRoutingChanged")
            {
                var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
                if (conversation is null) return;
                var current = SenorArroz.Application.Common.Helpers.WhatsAppConversationMapper.ToDto(conversation);
                var allowed = WhatsAppRealtimeGroupResolver.Resolve(_tenant.TenantId, conversation.BranchId, current);
                var removed = groups.Except(allowed).ToArray();
                if (removed.Length > 0) await _hubContext.Clients.Groups(removed).SendAsync(name,
                    new { branchId = conversation.OperationalBranchId ?? conversation.BranchId,
                        conversation = new { id = conversationId, isCentralChannel = current.IsCentralChannel,
                            operationalBranchId = conversation.OperationalBranchId, assignedUserId = conversation.AssignedUserId } }, ct);
                var recipients = groups.Intersect(allowed).ToArray();
                if (recipients.Length > 0) await _hubContext.Clients.Groups(recipients).SendAsync(name, payload, ct);
                return;
            }
            await _hubContext.Clients.Groups(groups).SendAsync(name, payload, ct);
            return;
        }
        _db.WhatsAppRealtimeEvents.Add(new WhatsAppRealtimeEvent
        {
            TenantId = _tenant.TenantId, ConversationId = conversationId, EventName = name,
            Payload = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Groups = groups.ToArray()
        });
        await _db.SaveChangesAsync(ct);
        await WhatsAppDatabaseSignal.NotifyAsync(_db.Database, _logger, ct);
    }

    private async Task ResolveCentralRoutingAsync(WhatsAppConversationDto conversation, CancellationToken cancellationToken)
    {
        var state = await _db.WhatsAppConversations.AsNoTracking().Where(x => x.Id == conversation.Id)
            .Select(x => new
            {
                IsCentral = x.ChannelSettingId.HasValue, x.OperationalBranchId,
                OperationalBranchName = x.OperationalBranch != null ? x.OperationalBranch.Name : null,
                x.AssignedUserId, Advisor = x.AssignedUser != null ? x.AssignedUser.Name : null,
                Line = x.ChannelSetting != null ? x.ChannelSetting.DisplayPhoneNumber
                    : x.BranchLineId != null ? x.BranchLine!.DisplayPhoneNumber
                        : x.Branch.WhatsAppSettings.Where(s => !s.CoexistenceEnabled).Select(s => s.DisplayPhoneNumber).FirstOrDefault(),
                Commerce = _db.WhatsAppCommerceSessions.Where(s => s.ConversationId == x.Id).OrderByDescending(s => s.Id)
                    .Select(s => new { s.CommercialStatus, s.OrderId }).FirstOrDefault()
            }).SingleOrDefaultAsync(cancellationToken);
        if (state is null) return;
        conversation.CommercialStatus = state.Commerce?.CommercialStatus ?? "not_started";
        conversation.CommercialOrderId = state.Commerce?.OrderId;
        conversation.SenderLine = state.Line;
        conversation.AssignedUserName = state.Advisor;
        conversation.IsCentralChannel = state.IsCentral;
        conversation.OperationalBranchId = state.OperationalBranchId;
        conversation.OperationalBranchName = state.OperationalBranchName;
        conversation.AssignedUserId = state.AssignedUserId;
    }
}
