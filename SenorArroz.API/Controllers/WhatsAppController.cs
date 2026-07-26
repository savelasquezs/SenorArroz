using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController : ControllerBase
{
    private sealed record WhatsAppAwayMessageDispatch(
        WhatsAppMessage Message,
        DateTime ClosedPeriodStartedAtUtc,
        string Text);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;
    private readonly IWhatsAppCloudClient _whatsAppCloudClient;
    private readonly IWhatsAppNotificationService _whatsAppNotificationService;
    private readonly IFirebaseGcsStorage _firebaseStorage;
    private readonly FirebaseStorageOptions _firebaseOptions;
    private readonly WhatsAppCloudOptions _whatsAppOptions;
    private readonly ILogger<WhatsAppController> _logger;
    private readonly WhatsAppAttentionService _attentionService;
    private readonly IWhatsAppAiWorkQueue _aiWorkQueue;
    private readonly IWhatsAppAutomaticMessageSender _automaticMessageSender;
    private readonly IBranchBusinessHoursService _businessHoursService;
    private readonly WhatsAppAwayMessageService _awayMessageService;
    private readonly int _aiMaxPersistentAttempts;

    public WhatsAppController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IClock clock,
        IWhatsAppCloudClient whatsAppCloudClient,
        IWhatsAppNotificationService whatsAppNotificationService,
        IFirebaseGcsStorage firebaseStorage,
        IOptions<FirebaseStorageOptions> firebaseOptions,
        IOptions<WhatsAppCloudOptions> whatsAppOptions,
        IOptions<WhatsAppAiOrchestratorOptions> aiOptions,
        ILogger<WhatsAppController> logger,
        WhatsAppAttentionService attentionService,
        IWhatsAppAiWorkQueue aiWorkQueue,
        IWhatsAppAutomaticMessageSender automaticMessageSender,
        IBranchBusinessHoursService businessHoursService,
        WhatsAppAwayMessageService awayMessageService)
    {
        _db = db;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _clock = clock;
        _whatsAppCloudClient = whatsAppCloudClient;
        _whatsAppNotificationService = whatsAppNotificationService;
        _firebaseStorage = firebaseStorage;
        _firebaseOptions = firebaseOptions.Value;
        _whatsAppOptions = whatsAppOptions.Value;
        _aiMaxPersistentAttempts = Math.Max(1, aiOptions.Value.MaxPersistentAttempts);
        _logger = logger;
        _attentionService = attentionService;
        _aiWorkQueue = aiWorkQueue;
        _automaticMessageSender = automaticMessageSender;
        _businessHoursService = businessHoursService;
        _awayMessageService = awayMessageService;
    }

    [HttpGet("status")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppStatusDto>>> GetStatus(CancellationToken cancellationToken)
    {
        var branchIds = await GetAllowedVerifiedBranchIdsQuery()
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<WhatsAppStatusDto>.SuccessResponse(new WhatsAppStatusDto
        {
            Enabled = branchIds.Count > 0,
            BranchIds = branchIds
        }, "Estado de WhatsApp obtenido."));
    }

    [HttpGet("unread-summary")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppUnreadSummaryDto>>> GetUnreadSummary(CancellationToken cancellationToken)
    {
        var branchIds = await GetAllowedVerifiedBranchIdsQuery().ToListAsync(cancellationToken);
        if (branchIds.Count == 0)
        {
            return Ok(ApiResponse<WhatsAppUnreadSummaryDto>.SuccessResponse(
                new WhatsAppUnreadSummaryDto(),
                "Resumen de WhatsApp obtenido."));
        }

        var query = _db.WhatsAppConversations
            .AsNoTracking()
            .Where(x => branchIds.Contains(x.BranchId) && x.UnreadCount > 0);

        var unreadConversations = await query.CountAsync(cancellationToken);
        if (unreadConversations == 0)
        {
            return Ok(ApiResponse<WhatsAppUnreadSummaryDto>.SuccessResponse(
                new WhatsAppUnreadSummaryDto(),
                "Resumen de WhatsApp obtenido."));
        }

        var totalUnread = await query.SumAsync(x => x.UnreadCount, cancellationToken);
        var latestMessageAt = await query
            .Select(x => x.LastMessageAt)
            .MaxAsync(cancellationToken);

        return Ok(ApiResponse<WhatsAppUnreadSummaryDto>.SuccessResponse(new WhatsAppUnreadSummaryDto
        {
            TotalUnread = totalUnread,
            UnreadConversations = unreadConversations,
            LatestMessageAt = latestMessageAt
        }, "Resumen de WhatsApp obtenido."));
    }

    [HttpGet("quick-replies")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppQuickReplyDto>>>> GetQuickReplies(
        [FromQuery] WhatsAppQuickReplySearchDto search,
        CancellationToken cancellationToken)
    {
        var query = _db.WhatsAppQuickReplies
            .AsNoTracking()
            .Include(x => x.Branch)
            .AsQueryable();

        var branchId = _branchContext.RequireBranch(search.BranchId);
        query = query.Where(x => x.BranchId == branchId);

        if (search.ActiveOnly)
            query = query.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim().ToLowerInvariant().TrimStart('/');
            query = query.Where(x =>
                x.Shortcut.ToLower().Contains(term)
                || x.MessageTemplate.ToLower().Contains(term));
        }

        var replies = await query
            .OrderByDescending(x => x.UsageCount)
            .ThenBy(x => x.Shortcut)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<WhatsAppQuickReplyDto>>.SuccessResponse(
            replies.Select(ToQuickReplyDto).ToList(),
            "Respuestas rápidas obtenidas."));
    }

    [HttpPost("templates/sync")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppTemplateSyncResultDto>>> SyncTemplates(
        [FromBody] SyncWhatsAppTemplatesDto? dto,
        CancellationToken cancellationToken)
    {
        var credentials = await ResolveTemplateCredentialsAsync(dto?.BranchId, requireBusinessAccount: true, cancellationToken);
        if (credentials.Forbidden)
            return Forbid();
        if (credentials.ErrorMessage is not null)
            return BadRequest(ApiResponse<WhatsAppTemplateSyncResultDto>.ErrorResponse(credentials.ErrorMessage));

        var result = await _whatsAppCloudClient.GetMessageTemplatesAsync(
            credentials.BusinessAccountId!,
            credentials.AccessToken,
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("WhatsApp template sync failed for business account {BusinessAccountId}: {Error}", credentials.BusinessAccountId, result.ErrorMessage);
            return BadRequest(ApiResponse<WhatsAppTemplateSyncResultDto>.ErrorResponse(result.ErrorMessage ?? "No se pudieron sincronizar las plantillas."));
        }

        var created = 0;
        var updated = 0;
        foreach (var metaTemplate in result.Templates)
        {
            var template = await _db.WhatsAppTemplates
                .FirstOrDefaultAsync(x => x.MetaTemplateId == metaTemplate.MetaTemplateId, cancellationToken);

            if (template is null)
            {
                template = new WhatsAppTemplate { MetaTemplateId = metaTemplate.MetaTemplateId };
                _db.WhatsAppTemplates.Add(template);
                created++;
            }
            else
            {
                updated++;
            }

            template.BranchId = credentials.BranchId;
            template.BusinessAccountId = credentials.BusinessAccountId;
            template.Name = metaTemplate.Name;
            template.Language = metaTemplate.Language;
            template.Category = metaTemplate.Category;
            template.Status = metaTemplate.Status;
            template.Components = metaTemplate.ComponentsJson;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var response = new WhatsAppTemplateSyncResultDto
        {
            Synced = result.Templates.Count,
            Created = created,
            Updated = updated
        };

        _logger.LogInformation(
            "WhatsApp templates synced. BusinessAccountId={BusinessAccountId} Synced={Synced} Created={Created} Updated={Updated}",
            credentials.BusinessAccountId,
            response.Synced,
            response.Created,
            response.Updated);

        return Ok(ApiResponse<WhatsAppTemplateSyncResultDto>.SuccessResponse(response, "Plantillas sincronizadas."));
    }

    [HttpGet("templates")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppTemplateDto>>>> GetTemplates(
        [FromQuery] WhatsAppTemplateSearchDto search,
        CancellationToken cancellationToken)
    {
        var query = _db.WhatsAppTemplates
            .AsNoTracking()
            .Include(x => x.Branch)
            .AsQueryable();

        var branchId = _branchContext.RequireBranch(search.BranchId);
        query = query.Where(x => x.BranchId == branchId || x.BranchId == null);

        if (!string.IsNullOrWhiteSpace(search.Status))
        {
            var status = search.Status.Trim().ToUpperInvariant();
            query = query.Where(x => x.Status.ToUpper() == status);
        }

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Name.ToLower().Contains(term)
                || x.Language.ToLower().Contains(term)
                || x.Category.ToLower().Contains(term));
        }

        var templates = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Language)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<WhatsAppTemplateDto>>.SuccessResponse(
            templates.Select(ToTemplateDto).ToList(),
            "Plantillas obtenidas."));
    }

    [HttpPost("send-template")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppTemplateSendResultDto>>> SendTemplate(
        [FromBody] SendWhatsAppTemplateDto dto,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = (dto.Language ?? string.Empty).Trim();
        var templateName = (dto.TemplateName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(templateName))
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse("Debe seleccionar una plantilla."));
        if (string.IsNullOrWhiteSpace(normalizedLanguage))
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse("Debe indicar el idioma de la plantilla."));

        var template = await _db.WhatsAppTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == templateName && x.Language == normalizedLanguage, cancellationToken);
        if (template is null)
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse("La plantilla no existe localmente. Sincronice plantillas primero."));
        if (!string.Equals(template.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse("La plantilla no está aprobada en Meta."));

        var expectedParameters = CountBodyTemplateParameters(template.Components);
        var parameters = dto.Parameters?.Select(x => x ?? string.Empty).ToList() ?? [];
        if (expectedParameters != parameters.Count)
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse($"La plantilla requiere {expectedParameters} parámetro(s) y se recibieron {parameters.Count}."));

        var branchId = dto.BranchId ?? template.BranchId;
        var credentials = await ResolveTemplateCredentialsAsync(branchId, requireBusinessAccount: false, cancellationToken);
        if (credentials.Forbidden)
            return Forbid();
        if (credentials.ErrorMessage is not null)
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse(credentials.ErrorMessage));

        var recipients = await ResolveTemplateRecipientsAsync(dto, credentials.BranchId, cancellationToken);
        if (recipients.Forbidden)
            return Forbid();
        if (recipients.ErrorMessage is not null)
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse(recipients.ErrorMessage));

        var response = new WhatsAppTemplateSendResultDto();
        foreach (var recipient in recipients.Recipients)
        {
            var result = await _whatsAppCloudClient.SendTemplateMessageAsync(
                credentials.PhoneNumberId!,
                credentials.AccessToken,
                recipient.PhoneNumber,
                template.Name,
                template.Language,
                parameters,
                cancellationToken);

            if (result.Success)
            {
                response.SentCount++;
                if (!string.IsNullOrWhiteSpace(result.WhatsAppMessageId))
                    response.MessageIds.Add(result.WhatsAppMessageId);

                if (credentials.BranchId.HasValue)
                    await PersistOutboundTemplateMessageAsync(credentials.BranchId.Value, recipient.PhoneNumber, recipient.Customer, template, parameters, result.WhatsAppMessageId, cancellationToken);
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add($"{FormatPhoneForError(recipient.PhoneNumber)}: {result.ErrorMessage ?? "Meta rechazó el envío."}");
                _logger.LogWarning("WhatsApp template send failed. Template={Template} To={To} Error={Error}", template.Name, recipient.PhoneNumber, result.ErrorMessage);
            }
        }

        response.Success = response.SentCount > 0 && response.FailedCount == 0;
        var message = response.FailedCount == 0
            ? "Plantilla enviada."
            : $"Plantillas enviadas con errores. Enviadas: {response.SentCount}, fallidas: {response.FailedCount}.";

        return response.SentCount > 0
            ? Ok(ApiResponse<WhatsAppTemplateSendResultDto>.SuccessResponse(response, message))
            : BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse(response.Errors.FirstOrDefault() ?? "No se pudo enviar la plantilla."));
    }

    [HttpGet("quick-replies/{id:int}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppQuickReplyDto>>> GetQuickReply(
        int id,
        CancellationToken cancellationToken)
    {
        var reply = await _db.WhatsAppQuickReplies
            .AsNoTracking()
            .Include(x => x.Branch)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (reply is null)
            return NotFound(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("Respuesta rápida no encontrada."));
        if (!CanAccessBranch(reply.BranchId))
            return Forbid();

        return Ok(ApiResponse<WhatsAppQuickReplyDto>.SuccessResponse(ToQuickReplyDto(reply), "Respuesta rápida obtenida."));
    }

    [HttpPost("quick-replies")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppQuickReplyDto>>> CreateQuickReply(
        [FromBody] UpsertWhatsAppQuickReplyDto dto,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateQuickReplyPayloadAsync(dto, null, cancellationToken);
        if (validation.Error is not null)
            return validation.Error;

        var reply = new WhatsAppQuickReply
        {
            BranchId = validation.BranchId,
            Shortcut = validation.Shortcut,
            MessageTemplate = dto.MessageTemplate.Trim(),
            IsActive = dto.IsActive
        };

        _db.WhatsAppQuickReplies.Add(reply);
        await _db.SaveChangesAsync(cancellationToken);

        var saved = await _db.WhatsAppQuickReplies
            .AsNoTracking()
            .Include(x => x.Branch)
            .FirstAsync(x => x.Id == reply.Id, cancellationToken);

        return CreatedAtAction(
            nameof(GetQuickReply),
            new { id = reply.Id },
            ApiResponse<WhatsAppQuickReplyDto>.SuccessResponse(ToQuickReplyDto(saved), "Respuesta rápida creada."));
    }

    [HttpPut("quick-replies/{id:int}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppQuickReplyDto>>> UpdateQuickReply(
        int id,
        [FromBody] UpsertWhatsAppQuickReplyDto dto,
        CancellationToken cancellationToken)
    {
        var reply = await _db.WhatsAppQuickReplies
            .Include(x => x.Branch)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (reply is null)
            return NotFound(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("Respuesta rápida no encontrada."));
        if (!CanAccessBranch(reply.BranchId))
            return Forbid();

        var validation = await ValidateQuickReplyPayloadAsync(dto, id, cancellationToken);
        if (validation.Error is not null)
            return validation.Error;

        reply.BranchId = validation.BranchId;
        reply.Shortcut = validation.Shortcut;
        reply.MessageTemplate = dto.MessageTemplate.Trim();
        reply.IsActive = dto.IsActive;

        await _db.SaveChangesAsync(cancellationToken);

        var saved = await _db.WhatsAppQuickReplies
            .AsNoTracking()
            .Include(x => x.Branch)
            .FirstAsync(x => x.Id == reply.Id, cancellationToken);

        return Ok(ApiResponse<WhatsAppQuickReplyDto>.SuccessResponse(ToQuickReplyDto(saved), "Respuesta rápida actualizada."));
    }

    [HttpDelete("quick-replies/{id:int}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse>> DeleteQuickReply(
        int id,
        CancellationToken cancellationToken)
    {
        var reply = await _db.WhatsAppQuickReplies
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (reply is null)
            return NotFound(ApiResponse.Error("Respuesta rápida no encontrada."));
        if (!CanAccessBranch(reply.BranchId))
            return Forbid();

        _db.WhatsAppQuickReplies.Remove(reply);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse.Success("Respuesta rápida eliminada."));
    }

    [HttpGet("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyWebhook(CancellationToken cancellationToken)
    {
        var mode = Request.Query["hub.mode"].FirstOrDefault();
        var challenge = Request.Query["hub.challenge"].FirstOrDefault();
        var verifyToken = Request.Query["hub.verify_token"].FirstOrDefault();

        if (!string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(challenge)
            || string.IsNullOrWhiteSpace(verifyToken))
        {
            return Forbid();
        }

        var exists = await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .AnyAsync(x => x.WebhookVerifyToken == verifyToken, cancellationToken);

        if (!exists)
        {
            _logger.LogWarning("WhatsApp webhook verification failed for verify token.");
            return Forbid();
        }

        _logger.LogInformation("WhatsApp webhook verified by Meta.");
        return Content(challenge, "text/plain");
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawPayload))
            rawPayload = "{}";

        var webhookEvent = new WhatsAppWebhookEvent
        {
            EventType = "webhook",
            RawPayload = rawPayload,
            Processed = false
        };
        _db.WhatsAppWebhookEvents.Add(webhookEvent);
        var createdMessages = new List<WhatsAppMessage>();
        var aiProcessingChanges = new List<WhatsAppMessage>();
        var awayMessageDispatches = new List<WhatsAppAwayMessageDispatch>();

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var processedAny = await ProcessWebhookPayloadAsync(
                document.RootElement,
                webhookEvent,
                createdMessages,
                aiProcessingChanges,
                awayMessageDispatches,
                cancellationToken);
            webhookEvent.Processed = processedAny;
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var message in createdMessages.Where(x => x.Id > 0))
            {
                await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
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
                if (!_aiWorkQueue.TryEnqueue(message.ConversationId, message.Id))
                    _logger.LogWarning("WhatsApp AI queue full; message remains pending. ConversationId={ConversationId} MessageId={MessageId}", message.ConversationId, message.Id);
            }
            foreach (var messageId in aiProcessingChanges.Select(x => x.Id).Where(x => x > 0).Distinct())
                await NotifyWhatsAppAiProcessingChangedAsync(messageId, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid WhatsApp webhook payload.");
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook.");
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok();
    }

    [HttpGet("conversations")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppConversationDto>>>> GetConversations(
        [FromQuery] WhatsAppConversationSearchDto search,
        CancellationToken cancellationToken)
    {
        var allowedBranchIds = await GetAllowedVerifiedBranchIdsQuery().ToListAsync(cancellationToken);
        if (search.BranchId.HasValue && !allowedBranchIds.Contains(search.BranchId.Value))
            return Forbid();

        var branchIds = search.BranchId.HasValue ? [search.BranchId.Value] : allowedBranchIds;
        var query = _db.WhatsAppConversations
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Customer)
            .Where(x => branchIds.Contains(x.BranchId));

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.PhoneNumber.ToLower().Contains(term)
                || (x.ContactName != null && x.ContactName.ToLower().Contains(term))
                || (x.Customer != null && x.Customer.Name.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(search.Status) && TryParseConversationStatus(search.Status, out var status))
            query = query.Where(x => x.Status == status);

        if (search.UnreadOnly == true)
            query = query.Where(x => x.UnreadCount > 0);

        var conversationEntities = await query
            .OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var assignedIds = conversationEntities.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value).Distinct().ToList();
        var assignedNames = await _db.Users.AsNoTracking().Where(x => assignedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var waitingIds = conversationEntities
            .Where(x => x.AttentionMode == WhatsAppAttentionMode.WaitingForHuman)
            .Select(x => x.Id)
            .ToList();
        var transferredMessages = await _db.WhatsAppMessages
            .AsNoTracking()
            .Where(x => waitingIds.Contains(x.ConversationId)
                && x.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman
                && x.AiProcessingError != null)
            .Select(x => new { x.ConversationId, x.Timestamp, x.Id, x.AiProcessingError })
            .ToListAsync(cancellationToken);
        var attentionReasons = transferredMessages
            .GroupBy(x => x.ConversationId)
            .ToDictionary(
                x => x.Key,
                x => WhatsAppAiDiagnosticsMapper.SanitizeTechnicalDetail(x
                    .OrderByDescending(message => message.Timestamp)
                    .ThenByDescending(message => message.Id)
                    .First().AiProcessingError));
        var conversations = conversationEntities.Select(x => ToConversationDto(
            x,
            x.AssignedUserId.HasValue ? assignedNames.GetValueOrDefault(x.AssignedUserId.Value) : null,
            attentionReasons.GetValueOrDefault(x.Id))).ToList();

        return Ok(ApiResponse<IReadOnlyList<WhatsAppConversationDto>>.SuccessResponse(conversations, "Conversaciones obtenidas."));
    }

    [HttpGet("conversations/{conversationId:int}/messages")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppMessageDto>>>> GetMessages(
        int conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<IReadOnlyList<WhatsAppMessageDto>>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();

        if (conversation.UnreadCount > 0)
        {
            conversation.UnreadCount = 0;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var messageEntities = await _db.WhatsAppMessages
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await EnsureMediaDownloadUrlsAsync(messageEntities, cancellationToken);
        var messages = messageEntities.Select(ToMessageDto).ToList();

        return Ok(ApiResponse<IReadOnlyList<WhatsAppMessageDto>>.SuccessResponse(messages, "Mensajes obtenidos."));
    }

    [HttpGet("conversations/{conversationId:int}/order-draft")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppOrderDraftDto>>> GetOrderDraft(
        int conversationId,
        [FromServices] IWhatsAppSimpleOrderStateService orderState,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations
            .AsNoTracking()
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppOrderDraftDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();

        var state = await orderState.LoadAsync(conversationId, cancellationToken);
        var summary = await orderState.BuildSummaryAsync(conversation.BranchId, state, cancellationToken);
        WhatsAppOrderDraftAddressDto? selectedAddress = null;
        if (state.SelectedAddressId.HasValue && conversation.CustomerId.HasValue)
        {
            selectedAddress = await _db.Addresses.AsNoTracking()
                .Where(x => x.Id == state.SelectedAddressId.Value
                    && x.CustomerId == conversation.CustomerId.Value
                    && x.Customer.BranchId == conversation.BranchId)
                .Select(x => new WhatsAppOrderDraftAddressDto(
                    x.Id,
                    x.AddressText,
                    x.AdditionalInfo,
                    x.Neighborhood.Name,
                    x.DeliveryFee))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var deliveryFee = state.OrderType == OrderType.Delivery ? selectedAddress?.DeliveryFee ?? 0 : 0;
        var notes = state.Items.ToDictionary(x => x.ProductId, x => x.Notes);
        var dto = new WhatsAppOrderDraftDto
        {
            ConversationId = conversation.Id,
            BranchId = conversation.BranchId,
            CustomerId = conversation.CustomerId,
            CustomerName = conversation.Customer?.Name ?? conversation.ContactName,
            PhoneNumber = conversation.PhoneNumber,
            OrderType = state.OrderType?.ToString().ToLowerInvariant(),
            SelectedAddressId = selectedAddress?.Id,
            SelectedAddress = selectedAddress,
            Items = summary.Items.Select(x => new WhatsAppOrderDraftItemDto(
                x.ProductId,
                x.Name,
                x.Quantity,
                x.UnitPrice,
                x.Subtotal,
                notes.GetValueOrDefault(x.ProductId),
                x.Available)).ToList(),
            Activities = state.Activities
                .OrderByDescending(x => x.Timestamp)
                .Select(x => new WhatsAppOrderDraftActivityDto(x.Type, x.Message, x.Timestamp))
                .ToList(),
            Subtotal = summary.Subtotal,
            DeliveryFee = deliveryFee,
            Total = summary.Subtotal + deliveryFee,
            TotalItems = summary.TotalItems,
            UpdatedAt = state.UpdatedAt == default ? null : AsUtc(state.UpdatedAt)
        };
        return Ok(ApiResponse<WhatsAppOrderDraftDto>.SuccessResponse(dto, "Draft de WhatsApp obtenido."));
    }

    [HttpPut("conversations/{conversationId:int}/order-draft/fulfillment")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateOrderDraftFulfillment(
        int conversationId,
        [FromBody] UpdateWhatsAppOrderDraftFulfillmentDto request,
        [FromServices] IWhatsAppSimpleOrderStateService orderState,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return NotFound(ApiResponse<string>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken)) return Forbid();
        if (request.OrderType is not ("onsite" or "delivery"))
            return BadRequest(ApiResponse<string>.ErrorResponse("Tipo de pedido inválido."));

        Address? address = null;
        if (request.OrderType == "delivery" && request.AddressId.HasValue && conversation.CustomerId.HasValue)
        {
            address = await _db.Addresses.AsNoTracking().Include(x => x.Neighborhood)
                .FirstOrDefaultAsync(x => x.Id == request.AddressId.Value
                    && x.CustomerId == conversation.CustomerId.Value
                    && x.Customer.BranchId == conversation.BranchId,
                    cancellationToken);
            if (address is null) return BadRequest(ApiResponse<string>.ErrorResponse("La dirección no pertenece al cliente de la conversación."));
        }

        var state = await orderState.LoadAsync(conversationId, cancellationToken);
        state.OrderType = request.OrderType == "onsite" ? OrderType.Onsite : OrderType.Delivery;
        state.SelectedAddressId = request.OrderType == "delivery" ? address?.Id : null;
        state.Activities.Add(new()
        {
            Type = "manual_fulfillment",
            Message = request.OrderType == "onsite"
                ? "Un asesor configuró el pedido para recoger en el local."
                : address is null
                    ? "Un asesor dejó pendiente la dirección de domicilio."
                    : $"Un asesor seleccionó la dirección {address.AddressText}, {address.Neighborhood.Name}.",
            Timestamp = _clock.UtcNow
        });
        await orderState.SaveAsync(conversationId, state, cancellationToken);
        return Ok(ApiResponse<string>.SuccessResponse("ok", "Draft actualizado."));
    }

    [HttpPost("conversations/{conversationId:int}/customer")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppConversationDto>>> LinkCustomer(
        int conversationId,
        [FromBody] LinkWhatsAppConversationCustomerDto dto,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations
            .Include(x => x.Branch)
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppConversationDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();

        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.CustomerId && x.BranchId == conversation.BranchId && x.Active, cancellationToken);
        if (customer is null)
            return BadRequest(ApiResponse<WhatsAppConversationDto>.ErrorResponse("Cliente no encontrado para la sucursal de esta conversación."));

        conversation.CustomerId = customer.Id;
        conversation.ContactName = customer.Name;
        conversation.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        conversation.Customer = customer;
        return Ok(ApiResponse<WhatsAppConversationDto>.SuccessResponse(ToConversationDto(conversation), "Cliente vinculado a la conversación."));
    }

    [HttpGet("conversations/{conversationId:int}/attention")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> GetAttention(int conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return NotFound(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken)) return Forbid();
        return Ok(ApiResponse<WhatsAppAttentionDto>.SuccessResponse(await ToAttentionDtoAsync(conversation, cancellationToken)));
    }

    [HttpPost("conversations/{conversationId:int}/take")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> TakeConversation(int conversationId, CancellationToken ct) => ChangeAttention(conversationId, "take", ct);
    [HttpPost("conversations/{conversationId:int}/return-to-ai")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> ReturnConversationToAi(int conversationId, CancellationToken ct) => ChangeAttention(conversationId, "ai", ct);
    [HttpPost("conversations/{conversationId:int}/pause-ai")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> PauseConversationAi(int conversationId, CancellationToken ct) => ChangeAttention(conversationId, "pause", ct);
    [HttpPost("conversations/{conversationId:int}/request-human")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> RequestConversationHuman(int conversationId, CancellationToken ct) => ChangeAttention(conversationId, "request-human", ct);
    [HttpPost("conversations/{conversationId:int}/close")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> CloseConversation(int conversationId, CancellationToken ct) => ChangeAttention(conversationId, "close", ct);
    [HttpPost("conversations/{conversationId:int}/reopen")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> ReopenConversation(int conversationId, CancellationToken ct) => ChangeAttention(conversationId, "reopen", ct);

    [HttpDelete("conversations/{conversationId:int}/test-context")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<WhatsAppConversationDto>>> ResetConversationForTesting(
        int conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations
            .Include(x => x.Branch)
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppConversationDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();

        var processing = await _db.WhatsAppMessages.AsNoTracking().AnyAsync(x =>
            x.ConversationId == conversationId
            && (x.AiProcessingStatus == WhatsAppAiProcessingStatus.Pending
                || x.AiProcessingStatus == WhatsAppAiProcessingStatus.Processing
                || x.AiProcessingStatus == WhatsAppAiProcessingStatus.ResponseGenerated
                || x.AiProcessingStatus == WhatsAppAiProcessingStatus.Sending), cancellationToken);
        if (processing)
            return Conflict(ApiResponse<WhatsAppConversationDto>.ErrorResponse(
                "Espera a que la IA termine de procesar antes de reiniciar la prueba."));

        var invocations = await _db.WhatsAppAiInvocations
            .Where(x => x.ConversationId == conversationId)
            .ToListAsync(cancellationToken);
        var messages = await _db.WhatsAppMessages
            .Where(x => x.ConversationId == conversationId)
            .ToListAsync(cancellationToken);
        _db.WhatsAppAiInvocations.RemoveRange(invocations);
        _db.WhatsAppMessages.RemoveRange(messages);

        var now = _clock.UtcNow;
        var aiActive = await IsBranchAiActiveAsync(conversation.BranchId, cancellationToken);
        conversation.Status = WhatsAppConversationStatus.Open;
        conversation.LastMessageAt = null;
        conversation.LastMessagePreview = null;
        conversation.UnreadCount = 0;
        conversation.AttentionMode = _attentionService.InitialMode(aiActive);
        conversation.AssignedUserId = null;
        conversation.AiPausedAt = null;
        conversation.HumanAssignedAt = null;
        conversation.ClosedAt = null;
        conversation.AttentionModeUpdatedAt = now;
        conversation.AttentionModeUpdatedByUserId = _currentUser.Id;
        conversation.AiOrderState = null;
        conversation.AiOrderStateUpdatedAt = null;
        conversation.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        var dto = ToConversationDto(conversation);
        await _whatsAppNotificationService.NotifyAttentionChangedAsync(conversation.BranchId, dto, cancellationToken);

        _logger.LogInformation(
            "WhatsApp test context reset ConversationId={ConversationId} BranchId={BranchId} UserId={UserId} DeletedMessages={DeletedMessages}",
            conversation.Id,
            conversation.BranchId,
            _currentUser.Id,
            messages.Count);

        return Ok(ApiResponse<WhatsAppConversationDto>.SuccessResponse(
            dto,
            "Contexto de prueba reiniciado. El cliente y sus direcciones se conservaron."));
    }

    private async Task<ActionResult<ApiResponse<WhatsAppAttentionDto>>> ChangeAttention(int conversationId, string action, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound(ApiResponse<WhatsAppAttentionDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, ct)) return Forbid();
        var aiActive = await IsBranchAiActiveAsync(conversation.BranchId, ct); var now = _clock.UtcNow; var userId = _currentUser.Id;
        var changed = action switch { "take" => _attentionService.Take(conversation, userId, now), "ai" => _attentionService.ReturnToAi(conversation, userId, now, aiActive), "pause" => _attentionService.Pause(conversation, userId, now), "request-human" => _attentionService.RequestHuman(conversation, userId, now), "close" => _attentionService.Close(conversation, userId, now), "reopen" => _attentionService.Reopen(conversation, userId, now, aiActive), _ => throw new ArgumentOutOfRangeException(nameof(action)) };
        if (changed) await _db.SaveChangesAsync(ct); var dto = await ToAttentionDtoAsync(conversation, ct);
        if (changed) await _whatsAppNotificationService.NotifyAttentionChangedAsync(conversation.BranchId, ToConversationDto(conversation, dto.AssignedUserName, dto.AttentionReason), ct);
        return Ok(ApiResponse<WhatsAppAttentionDto>.SuccessResponse(dto, "Estado de atención actualizado."));
    }

    [HttpPost("conversations/{conversationId:int}/messages")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMessage(
        int conversationId,
        [FromBody] SendWhatsAppMessageDto dto,
        CancellationToken cancellationToken)
    {
        var text = (dto.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El mensaje no puede estar vacío."));
        if (text.Length > 4096)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El mensaje no puede superar 4096 caracteres."));

        var conversation = await _db.WhatsAppConversations
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();
        if (conversation.AttentionMode == WhatsAppAttentionMode.Closed)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación está cerrada. Debes reabrirla antes de enviar mensajes."));

        var setting = await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == conversation.BranchId && x.IsActive && x.IsVerified, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));

        if (conversation.AttentionMode != WhatsAppAttentionMode.Human || conversation.AssignedUserId != _currentUser.Id)
        {
            var attentionChanged = _attentionService.Take(conversation, _currentUser.Id, _clock.UtcNow);
            if (attentionChanged)
            {
                await _db.SaveChangesAsync(cancellationToken);
                var attention = await ToAttentionDtoAsync(conversation, cancellationToken);
                await _whatsAppNotificationService.NotifyAttentionChangedAsync(conversation.BranchId, ToConversationDto(conversation, attention.AssignedUserName, attention.AttentionReason), cancellationToken);
            }
        }

        // La toma se persiste antes de llamar a Meta. Si Meta falla, la conversación permanece
        // asignada al empleado para que pueda revisar y reintentar de forma explícita.
        var timestamp = _clock.UtcNow;
        var result = await _whatsAppCloudClient.SendTextMessageAsync(
            setting.PhoneNumberId,
            setting.AccessToken,
            conversation.PhoneNumber,
            text,
            cancellationToken);

        var message = new WhatsAppMessage
        {
            ConversationId = conversation.Id,
            WhatsAppMessageId = result.WhatsAppMessageId,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = WhatsAppMessageType.Text,
            TextBody = text,
            Status = result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null,
            Timestamp = timestamp,
            RawPayload = JsonSerializer.Serialize(new
            {
                to = conversation.PhoneNumber,
                text,
                result.Success,
                result.WhatsAppMessageId,
                result.ErrorMessage
            })
        };
        _db.WhatsAppMessages.Add(message);

        conversation.LastMessageAt = timestamp;
        conversation.LastMessagePreview = text;
        await _db.SaveChangesAsync(cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("WhatsApp outbound message failed for conversation {ConversationId}: {Error}", conversationId, result.ErrorMessage);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar el mensaje."));
        }

        _logger.LogInformation("WhatsApp outbound message sent for conversation {ConversationId}", conversationId);
        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Mensaje enviado."));
    }

    [HttpPost("conversations/{conversationId:int}/messages/products/{productId:int}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendProductDetails(int conversationId, int productId, CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations.FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken)) return Forbid();
        var product = await _db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.CommercialProfile).FirstOrDefaultAsync(x => x.Id == productId && x.Category.BranchId == conversation.BranchId, cancellationToken);
        if (product is null) return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Producto no encontrado."));
        var setting = await _db.WhatsAppBranchSettings.AsNoTracking().FirstAsync(x => x.BranchId == conversation.BranchId && x.IsActive && x.IsVerified, cancellationToken);
        var serves = product.ServesPeopleMin == product.ServesPeopleMax && product.ServesPeopleMin.HasValue ? $"{product.ServesPeopleMin} {(product.ServesPeopleMin == 1 ? "persona" : "personas")}" : product.ServesPeopleMin.HasValue ? $"{product.ServesPeopleMin}-{product.ServesPeopleMax} personas" : null;
        var available = product.Active && (!product.Stock.HasValue || product.Stock > 0);
        var text = string.Join("\n", new[] { product.Name, product.CommercialProfile?.Description, string.IsNullOrWhiteSpace(product.CommercialProfile?.Ingredients) ? null : $"Ingredientes: {product.CommercialProfile.Ingredients}", serves is null ? null : $"Rinde para {serves}", $"Precio: ${product.Price:N0}", available ? "Disponible" : "No disponible" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var result = !string.IsNullOrWhiteSpace(product.CommercialProfile?.PhotoUrl)
            ? await _whatsAppCloudClient.SendImageLinkMessageAsync(setting.PhoneNumberId, setting.AccessToken, conversation.PhoneNumber, product.CommercialProfile.PhotoUrl, text, cancellationToken)
            : await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, conversation.PhoneNumber, text, cancellationToken);
        var now = _clock.UtcNow; var message = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = result.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = string.IsNullOrWhiteSpace(product.CommercialProfile?.PhotoUrl) ? WhatsAppMessageType.Text : WhatsAppMessageType.Image, TextBody = text, MediaUrl = product.CommercialProfile?.PhotoUrl, Status = result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed, Timestamp = now, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, RawPayload = JsonSerializer.Serialize(new { action = "send_product_details", productId, result.Success, result.ErrorMessage }) };
        _db.WhatsAppMessages.Add(message); conversation.LastMessageAt = now; conversation.LastMessagePreview = text; await _db.SaveChangesAsync(cancellationToken);
        if (!result.Success) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar el producto.")); await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Detalle de producto enviado."));
    }

    [HttpPost("conversations/{conversationId:int}/messages/menu")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMenu(int conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations.FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken)) return Forbid();
        var branch = await _db.Branches.AsNoTracking().FirstAsync(x => x.Id == conversation.BranchId, cancellationToken);
        if (string.IsNullOrWhiteSpace(branch.MenuImageUrl1) && string.IsNullOrWhiteSpace(branch.MenuImageUrl2))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Esta sucursal todavía no tiene una carta configurada."));
        var setting = await _db.WhatsAppBranchSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchId == conversation.BranchId && x.IsActive && x.IsVerified, cancellationToken);
        if (setting is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/public/menu?branchId={conversation.BranchId}";
        var text = $"Consulta nuestra carta aquí: {url}";
        var menuImages = new[] { branch.MenuImageUrl1, branch.MenuImageUrl2 }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
        var sentMessages = new List<WhatsAppMessage>();
        string? lastError = null;
        for (var index = 0; index < menuImages.Count; index++)
        {
            var caption = index == 0 ? "Nuestra carta actual" : "Carta (continuación)";
            var result = await _whatsAppCloudClient.SendImageLinkMessageAsync(setting.PhoneNumberId, setting.AccessToken, conversation.PhoneNumber, menuImages[index], caption, cancellationToken);
            if (!result.Success) { lastError = result.ErrorMessage; break; }
            var timestamp = _clock.UtcNow;
            var message = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = result.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = WhatsAppMessageType.Image, TextBody = caption, MediaUrl = menuImages[index], Status = WhatsAppMessageStatus.Sent, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, Timestamp = timestamp, RawPayload = JsonSerializer.Serialize(new { action = "send_menu", mode = "image", slot = index + 1, result.Success }) };
            _db.WhatsAppMessages.Add(message); sentMessages.Add(message);
        }
        var usedFallback = sentMessages.Count != menuImages.Count;
        if (usedFallback)
        {
            var fallback = await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, conversation.PhoneNumber, text, cancellationToken);
            if (fallback.Success)
            {
                var fallbackMessage = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = fallback.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = WhatsAppMessageType.Text, TextBody = text, Status = WhatsAppMessageStatus.Sent, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, Timestamp = _clock.UtcNow, RawPayload = JsonSerializer.Serialize(new { action = "send_menu", mode = "url_fallback", imageError = lastError }) };
                _db.WhatsAppMessages.Add(fallbackMessage); sentMessages.Add(fallbackMessage);
            }
            else if (sentMessages.Count == 0) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(fallback.ErrorMessage ?? lastError ?? "No se pudo enviar la carta."));
        }
        var lastMessage = sentMessages[^1]; conversation.LastMessageAt = lastMessage.Timestamp; conversation.LastMessagePreview = "Carta enviada"; await _db.SaveChangesAsync(cancellationToken);
        foreach (var sentMessage in sentMessages) await NotifyWhatsAppMessageCreatedAsync(sentMessage.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(lastMessage), usedFallback ? "Carta enviada; se usó el enlace como respaldo." : "Carta enviada como imagen."));
    }

    [HttpPost("conversations/{conversationId:int}/messages/quick-reply")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendQuickReply(
        int conversationId,
        [FromBody] SendWhatsAppQuickReplyDto dto,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();

        var quickReply = await _db.WhatsAppQuickReplies
            .FirstOrDefaultAsync(x => x.Id == dto.QuickReplyId && x.BranchId == conversation.BranchId && x.IsActive, cancellationToken);
        if (quickReply is null)
            return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Respuesta rápida no encontrada o inactiva para esta sucursal."));

        var text = RenderQuickReplyTemplate(quickReply.MessageTemplate, conversation).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La respuesta rápida no tiene contenido para enviar."));
        if (text.Length > 4096)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La respuesta rápida renderizada supera 4096 caracteres."));

        var setting = await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == conversation.BranchId && x.IsActive && x.IsVerified, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));

        var timestamp = _clock.UtcNow;
        var result = await _whatsAppCloudClient.SendTextMessageAsync(
            setting.PhoneNumberId,
            setting.AccessToken,
            conversation.PhoneNumber,
            text,
            cancellationToken);

        var message = new WhatsAppMessage
        {
            ConversationId = conversation.Id,
            WhatsAppMessageId = result.WhatsAppMessageId,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = WhatsAppMessageType.Text,
            TextBody = text,
            Status = result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null,
            Timestamp = timestamp,
            RawPayload = JsonSerializer.Serialize(new
            {
                to = conversation.PhoneNumber,
                text,
                quickReplyId = quickReply.Id,
                result.Success,
                result.WhatsAppMessageId,
                result.ErrorMessage
            })
        };
        _db.WhatsAppMessages.Add(message);

        conversation.LastMessageAt = timestamp;
        conversation.LastMessagePreview = text;
        quickReply.UsageCount += 1;
        quickReply.LastUsedAt = timestamp;
        await _db.SaveChangesAsync(cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("WhatsApp quick reply failed for conversation {ConversationId}: {Error}", conversationId, result.ErrorMessage);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar la respuesta rápida."));
        }

        _logger.LogInformation("WhatsApp quick reply sent for conversation {ConversationId}", conversationId);
        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Respuesta rápida enviada."));
    }

    [HttpPost("conversations/{conversationId:int}/messages/media")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(70_000_000)]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMediaMessage(
        int conversationId,
        IFormFile? file,
        [FromForm] string? caption,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Seleccione un archivo."));

        var conversation = await _db.WhatsAppConversations
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        if (!await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken))
            return Forbid();

        var setting = await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == conversation.BranchId && x.IsActive && x.IsVerified, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));

        var mediaType = MediaTypeFromContentType(file.ContentType, file.FileName);
        var fileName = SanitizeFileName(file.FileName);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();
        var timestamp = _clock.UtcNow;
        var trimmedCaption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (trimmedCaption?.Length > 4096)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El texto del archivo no puede superar 4096 caracteres."));

        var text = trimmedCaption ?? fileName;

        string? storageUrl = null;
        try
        {
            storageUrl = await SaveWhatsAppMediaToFirebaseAsync(
                conversation.BranchId,
                conversation.Id,
                bytes,
                fileName,
                contentType,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not upload outbound WhatsApp media to Firebase for conversation {ConversationId}", conversationId);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("No se pudo guardar el archivo en Firebase Storage."));
        }

        var uploadResult = await _whatsAppCloudClient.UploadMediaAsync(
            setting.PhoneNumberId,
            setting.AccessToken,
            bytes,
            fileName,
            contentType,
            cancellationToken);

        WhatsAppCloudSendResult sendResult = new(false, null, uploadResult.ErrorMessage);
        if (uploadResult.Success && !string.IsNullOrWhiteSpace(uploadResult.MediaId))
        {
            sendResult = await _whatsAppCloudClient.SendMediaMessageAsync(
                setting.PhoneNumberId,
                setting.AccessToken,
                conversation.PhoneNumber,
                MediaTypeToApi(mediaType),
                uploadResult.MediaId,
                trimmedCaption,
                fileName,
                cancellationToken);
        }

        var message = new WhatsAppMessage
        {
            ConversationId = conversation.Id,
            WhatsAppMessageId = sendResult.WhatsAppMessageId,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = mediaType,
            TextBody = text,
            MediaId = uploadResult.MediaId,
            MediaUrl = storageUrl,
            MediaMimeType = contentType,
            MediaFileName = fileName,
            MediaFileSize = bytes.LongLength,
            Status = sendResult.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null,
            Timestamp = timestamp,
            RawPayload = JsonSerializer.Serialize(new
            {
                to = conversation.PhoneNumber,
                mediaType = MediaTypeToApi(mediaType),
                mediaId = uploadResult.MediaId,
                fileName,
                caption = trimmedCaption,
                storageUrl,
                sendResult.Success,
                sendResult.WhatsAppMessageId,
                error = sendResult.ErrorMessage ?? uploadResult.ErrorMessage
            })
        };
        _db.WhatsAppMessages.Add(message);

        conversation.LastMessageAt = timestamp;
        conversation.LastMessagePreview = PreviewForMedia(mediaType, text);
        await _db.SaveChangesAsync(cancellationToken);

        if (!sendResult.Success)
        {
            _logger.LogWarning("WhatsApp outbound media failed for conversation {ConversationId}: {Error}", conversationId, sendResult.ErrorMessage ?? uploadResult.ErrorMessage);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(sendResult.ErrorMessage ?? uploadResult.ErrorMessage ?? "No se pudo enviar el archivo."));
        }

        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Archivo enviado."));
    }

    private async Task NotifyWhatsAppMessageCreatedAsync(int messageId, CancellationToken cancellationToken)
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

    private async Task EnsureMediaDownloadUrlsAsync(IReadOnlyList<WhatsAppMessage> messages, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.MediaUrl))
                continue;

            try
            {
                var downloadUrl = await _firebaseStorage.EnsureDownloadUrlAsync(message.MediaUrl, cancellationToken);
                if (!string.Equals(downloadUrl, message.MediaUrl, StringComparison.Ordinal))
                {
                    message.MediaUrl = downloadUrl;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not ensure Firebase download URL for WhatsApp message {MessageId}", message.Id);
            }
        }

        if (changed)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<int> GetAllowedVerifiedBranchIdsQuery()
    {
        var query = _db.WhatsAppBranchSettings
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsVerified);

        var branchId = _branchContext.RequireBranch();
        query = query.Where(x => x.BranchId == branchId);

        return query.Select(x => x.BranchId).Distinct();
    }

    private async Task<bool> CanAccessVerifiedBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        _branchContext.EnsureAccess(branchId);

        return await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId && x.IsActive && x.IsVerified, cancellationToken);
    }

    private async Task<TemplateCredentialsResolution> ResolveTemplateCredentialsAsync(
        int? branchId,
        bool requireBusinessAccount,
        CancellationToken cancellationToken)
    {
        branchId = _branchContext.RequireBranch(branchId);

        if (branchId.HasValue || !Roles.IsSuperadmin(_currentUser.Role))
        {
            var resolvedBranchId = branchId ?? _currentUser.BranchId;
            if (!CanAccessBranch(resolvedBranchId))
                return TemplateCredentialsResolution.Denied();

            var setting = await _db.WhatsAppBranchSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BranchId == resolvedBranchId && x.IsActive && x.IsVerified, cancellationToken);

            if (setting is null)
                return TemplateCredentialsResolution.Failed("WhatsApp no está activo y verificado para esta sucursal.");

            return new TemplateCredentialsResolution(
                false,
                null,
                resolvedBranchId,
                setting.AccessToken,
                setting.BusinessAccountId,
                setting.PhoneNumberId);
        }

        if (string.IsNullOrWhiteSpace(_whatsAppOptions.AccessToken))
            return TemplateCredentialsResolution.Failed("Falta configurar WHATSAPP_TOKEN o WhatsAppCloud:AccessToken.");
        if (string.IsNullOrWhiteSpace(_whatsAppOptions.PhoneNumberId))
            return TemplateCredentialsResolution.Failed("Falta configurar WHATSAPP_PHONE_NUMBER_ID o WhatsAppCloud:PhoneNumberId.");
        if (requireBusinessAccount && string.IsNullOrWhiteSpace(_whatsAppOptions.BusinessAccountId))
            return TemplateCredentialsResolution.Failed("Falta configurar WHATSAPP_BUSINESS_ACCOUNT_ID o WhatsAppCloud:BusinessAccountId.");

        return new TemplateCredentialsResolution(
            false,
            null,
            null,
            _whatsAppOptions.AccessToken!,
            _whatsAppOptions.BusinessAccountId,
            _whatsAppOptions.PhoneNumberId!);
    }

    private async Task<TemplateRecipientsResolution> ResolveTemplateRecipientsAsync(
        SendWhatsAppTemplateDto dto,
        int? branchId,
        CancellationToken cancellationToken)
    {
        var recipients = new List<TemplateRecipient>();

        if (!string.IsNullOrWhiteSpace(dto.To))
        {
            var phone = NormalizeWhatsAppPhone(dto.To);
            if (!IsInternationalWhatsAppPhone(phone))
                return TemplateRecipientsResolution.Failed("El número destino debe venir en formato internacional sin +. Ejemplo: 573001234567.");

            recipients.Add(new TemplateRecipient(phone, null));
        }

        if (dto.CustomerIds is { Count: > 0 })
        {
            if (!branchId.HasValue)
                return TemplateRecipientsResolution.Failed("Debe seleccionar una sucursal para enviar plantillas a clientes.");
            if (!CanAccessBranch(branchId.Value))
                return TemplateRecipientsResolution.Denied();

            var uniqueCustomerIds = dto.CustomerIds.Distinct().ToList();
            var customers = await _db.Customers
                .AsNoTracking()
                .Where(x => x.BranchId == branchId.Value && uniqueCustomerIds.Contains(x.Id) && x.Active)
                .ToListAsync(cancellationToken);

            foreach (var customer in customers)
            {
                var phone = NormalizeWhatsAppPhone(customer.Phone1);
                if (!IsInternationalWhatsAppPhone(phone))
                    phone = NormalizeWhatsAppPhone(customer.Phone2);
                if (!IsInternationalWhatsAppPhone(phone))
                    continue;

                recipients.Add(new TemplateRecipient(phone, customer));
            }
        }

        var unique = recipients
            .GroupBy(x => x.PhoneNumber)
            .Select(x => x.First())
            .ToList();

        if (unique.Count == 0)
            return TemplateRecipientsResolution.Failed("Debe indicar al menos un número válido o seleccionar clientes con teléfono internacional.");

        return new TemplateRecipientsResolution(false, null, unique);
    }

    private async Task PersistOutboundTemplateMessageAsync(
        int branchId,
        string phoneNumber,
        Customer? customer,
        WhatsAppTemplate template,
        IReadOnlyList<string> parameters,
        string? whatsAppMessageId,
        CancellationToken cancellationToken)
    {
        var timestamp = _clock.UtcNow;
        var conversation = await _db.WhatsAppConversations
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.PhoneNumber == phoneNumber, cancellationToken);

        if (conversation is null)
        {
            var aiActive = await IsBranchAiActiveAsync(branchId, cancellationToken);
            conversation = new WhatsAppConversation
            {
                BranchId = branchId,
                PhoneNumber = phoneNumber,
                CustomerId = customer?.Id,
                ContactName = customer?.Name,
                Status = WhatsAppConversationStatus.Open,
                AttentionMode = _attentionService.InitialMode(aiActive),
                AttentionModeUpdatedAt = timestamp
            };
            _db.WhatsAppConversations.Add(conversation);
        }

        conversation.CustomerId = customer?.Id ?? conversation.CustomerId;
        conversation.ContactName = customer?.Name ?? conversation.ContactName;
        conversation.LastMessageAt = timestamp;
        conversation.LastMessagePreview = $"Plantilla: {template.Name}";

        var message = new WhatsAppMessage
        {
            Conversation = conversation,
            WhatsAppMessageId = whatsAppMessageId,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = WhatsAppMessageType.Text,
            TextBody = BuildTemplateMessagePreview(template, parameters),
            Status = WhatsAppMessageStatus.Sent,
            SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null,
            Timestamp = timestamp,
            RawPayload = JsonSerializer.Serialize(new
            {
                template = template.Name,
                template.Language,
                parameters,
                whatsAppMessageId
            })
        };
        _db.WhatsAppMessages.Add(message);

        await _db.SaveChangesAsync(cancellationToken);
        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
    }

    private static string BuildTemplateMessagePreview(WhatsAppTemplate template, IReadOnlyList<string> parameters)
    {
        var body = ExtractBodyText(template.Components);
        if (string.IsNullOrWhiteSpace(body))
            return $"Plantilla: {template.Name}";

        for (var i = 0; i < parameters.Count; i++)
            body = body.Replace($"{{{{{i + 1}}}}}", parameters[i] ?? string.Empty, StringComparison.Ordinal);

        return body;
    }

    private static int CountBodyTemplateParameters(string componentsJson)
    {
        var body = ExtractBodyText(componentsJson);
        if (string.IsNullOrWhiteSpace(body))
            return 0;

        var matches = Regex.Matches(body, @"{{\s*(\d+)\s*}}");
        return matches
            .Select(x => int.TryParse(x.Groups[1].Value, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string? ExtractBodyText(string componentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(componentsJson) ? "[]" : componentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var component in document.RootElement.EnumerateArray())
            {
                var type = TryGetString(component, "type");
                if (string.Equals(type, "BODY", StringComparison.OrdinalIgnoreCase))
                    return TryGetString(component, "text");
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool IsInternationalWhatsAppPhone(string value)
    {
        return Regex.IsMatch(value, @"^[1-9]\d{7,14}$");
    }

    private static string FormatPhoneForError(string phoneNumber) => $"+{phoneNumber}";

    private bool CanAccessBranch(int branchId)
    {
        _branchContext.EnsureAccess(branchId);
        return true;
    }

    private async Task<(ActionResult<ApiResponse<WhatsAppQuickReplyDto>>? Error, int BranchId, string Shortcut)> ValidateQuickReplyPayloadAsync(
        UpsertWhatsAppQuickReplyDto dto,
        int? currentId,
        CancellationToken cancellationToken)
    {
        var branchId = _branchContext.RequireBranch(dto.BranchId);

        if (branchId <= 0)
            return (BadRequest(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("Debe seleccionar una sucursal.")), 0, string.Empty);
        if (!CanAccessBranch(branchId))
            return (Forbid(), 0, string.Empty);
        if (!await _db.Branches.AsNoTracking().AnyAsync(x => x.Id == branchId, cancellationToken))
            return (BadRequest(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("Sucursal no encontrada.")), 0, string.Empty);

        if (!TryNormalizeQuickReplyShortcut(dto.Shortcut, out var shortcut, out var shortcutError))
            return (BadRequest(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse(shortcutError)), 0, string.Empty);

        var template = (dto.MessageTemplate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(template))
            return (BadRequest(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("El mensaje no puede estar vacío.")), 0, string.Empty);
        if (template.Length > 4096)
            return (BadRequest(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("El mensaje no puede superar 4096 caracteres.")), 0, string.Empty);

        var duplicate = await _db.WhatsAppQuickReplies
            .AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId && x.Shortcut == shortcut && (!currentId.HasValue || x.Id != currentId.Value), cancellationToken);
        if (duplicate)
            return (BadRequest(ApiResponse<WhatsAppQuickReplyDto>.ErrorResponse("Ya existe una respuesta rápida con esa palabra en la sucursal.")), 0, string.Empty);

        return (null, branchId, shortcut);
    }

    private static bool TryNormalizeQuickReplyShortcut(string? value, out string shortcut, out string error)
    {
        shortcut = (value ?? string.Empty).Trim().TrimStart('/').Trim().ToLowerInvariant();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            error = "La palabra reservada es obligatoria.";
            return false;
        }

        if (shortcut.Length > 40)
        {
            error = "La palabra reservada no puede superar 40 caracteres.";
            return false;
        }

        if (!shortcut.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
        {
            error = "La palabra reservada solo puede tener letras, números, guion o guion bajo.";
            return false;
        }

        return true;
    }

    private static string RenderQuickReplyTemplate(string template, WhatsAppConversation conversation)
    {
        var customerName = conversation.Customer?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(customerName))
            customerName = conversation.ContactName?.Trim();
        if (string.IsNullOrWhiteSpace(customerName))
            customerName = "cliente";

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nombre_cliente"] = customerName,
            ["cliente"] = customerName,
            ["customerName"] = customerName,
            ["guestName"] = customerName,
            ["telefono"] = conversation.PhoneNumber,
        };

        return Regex.Replace(template, @"{{\s*([a-zA-Z0-9_]+)\s*}}", match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var replacement) ? replacement : match.Value;
        });
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

                var setting = await _db.WhatsAppBranchSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PhoneNumberId == phoneNumberId && x.IsActive && x.IsVerified, cancellationToken);

                if (setting is null)
                {
                    _logger.LogWarning("WhatsApp webhook ignored because phone number id {PhoneNumberId} is not configured.", phoneNumberId);
                    continue;
                }

                processedAny |= await ProcessInboundMessagesAsync(value, setting, webhookEvent, createdMessages, awayMessageDispatches, cancellationToken);
                processedAny |= await ProcessStatusesAsync(value, webhookEvent, aiProcessingChanges, cancellationToken);
            }
        }

        return processedAny;
    }

    private async Task<bool> ProcessInboundMessagesAsync(
        JsonElement value,
        WhatsAppBranchSetting setting,
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

            var from = NormalizeWhatsAppPhone(TryGetString(messageElement, "from"));
            var mediaPayload = TryGetMediaPayload(messageElement, messageType);
            var text = messageType == WhatsAppMessageType.Text
                ? TryGetTextBody(messageElement)
                : TryGetMediaCaptionOrName(mediaPayload, messageType);
            if (string.IsNullOrWhiteSpace(from))
                continue;

            var timestamp = ParseWhatsAppTimestamp(TryGetString(messageElement, "timestamp")) ?? _clock.UtcNow;
            var contactName = TryFindContactName(value, from);
            var customer = await FindCustomerByPhoneAsync(setting.BranchId, from, cancellationToken);

            var conversation = await _db.WhatsAppConversations
                .FirstOrDefaultAsync(x => x.BranchId == setting.BranchId && x.PhoneNumber == from, cancellationToken);
            if (conversation is null)
            {
                var aiActive = await IsBranchAiActiveAsync(setting.BranchId, cancellationToken);
                conversation = new WhatsAppConversation
                {
                    BranchId = setting.BranchId,
                    PhoneNumber = from,
                    Status = WhatsAppConversationStatus.Open,
                    AttentionMode = _attentionService.InitialMode(aiActive),
                    AttentionModeUpdatedAt = timestamp
                };
                _db.WhatsAppConversations.Add(conversation);
            }

            conversation.CustomerId = customer?.Id;
            conversation.ContactName = contactName ?? conversation.ContactName;
            conversation.LastMessageAt = timestamp;
            conversation.LastMessagePreview = PreviewForMedia(messageType, text ?? string.Empty);
            conversation.UnreadCount += 1;

            if (conversation.Id == 0 && messageType != WhatsAppMessageType.Text && mediaPayload is not null)
                await _db.SaveChangesAsync(cancellationToken);

            var inboundMedia = messageType == WhatsAppMessageType.Text || mediaPayload is null
                ? null
                : await DownloadAndStoreInboundMediaAsync(setting, conversation.BranchId, conversation.Id, mediaPayload.Value, messageType, cancellationToken);

            var aiProcessingStatus = WhatsAppAiProcessingStatus.Pending;
            DateTime? aiProcessedAt = null;
            string? aiProcessingError = null;
            DateTime? awayClosedPeriodStartedAtUtc = null;
            string? awayText = null;
            if (setting.AwayMessageEnabled)
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
                RawPayload = messageElement.GetRawText(),
                AiProcessingStatus = aiProcessingStatus,
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

    private async Task<Customer?> FindCustomerByPhoneAsync(int branchId, string whatsappPhone, CancellationToken cancellationToken)
    {
        var digits = OnlyDigits(whatsappPhone);
        var last10 = digits.Length > 10 ? digits[^10..] : digits;
        return await _db.Customers
            .AsNoTracking()
            .Where(x => x.BranchId == branchId && (x.Phone1 == digits || x.Phone2 == digits || x.Phone1 == last10 || x.Phone2 == last10))
            .OrderByDescending(x => x.Active)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? TryGetPhoneNumberId(JsonElement value)
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
        return null;
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

    private async Task<string> SaveWhatsAppMediaToFirebaseAsync(
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
        var objectName = $"{prefix}/{branchId}/{conversationId}/{_clock.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{ext}";
        return await _firebaseStorage.UploadPublicObjectAsync(content, objectName, contentType, cancellationToken);
    }

    private static string? TryFindContactName(JsonElement value, string phoneNumber)
    {
        if (!value.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var contact in contacts.EnumerateArray())
        {
            var waId = NormalizeWhatsAppPhone(TryGetString(contact, "wa_id"));
            if (!string.Equals(waId, phoneNumber, StringComparison.Ordinal))
                continue;

            return contact.TryGetProperty("profile", out var profile) ? TryGetString(profile, "name") : null;
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
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

    private static string NormalizeWhatsAppPhone(string? value) => OnlyDigits(value);

    private static string OnlyDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static bool TryParseConversationStatus(string value, out WhatsAppConversationStatus status)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "open":
                status = WhatsAppConversationStatus.Open;
                return true;
            case "pending":
                status = WhatsAppConversationStatus.Pending;
                return true;
            case "closed":
                status = WhatsAppConversationStatus.Closed;
                return true;
            case "archived":
                status = WhatsAppConversationStatus.Archived;
                return true;
            default:
                status = WhatsAppConversationStatus.Open;
                return false;
        }
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

    private static WhatsAppTemplateDto ToTemplateDto(WhatsAppTemplate template) => new()
    {
        Id = template.Id,
        BranchId = template.BranchId,
        BranchName = template.Branch?.Name,
        BusinessAccountId = template.BusinessAccountId,
        MetaTemplateId = template.MetaTemplateId,
        Name = template.Name,
        Language = template.Language,
        Category = template.Category,
        Status = template.Status,
        Components = template.Components,
        BodyParameterCount = CountBodyTemplateParameters(template.Components),
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt
    };

    private static WhatsAppQuickReplyDto ToQuickReplyDto(WhatsAppQuickReply reply) => new()
    {
        Id = reply.Id,
        BranchId = reply.BranchId,
        BranchName = reply.Branch?.Name,
        Shortcut = reply.Shortcut,
        MessageTemplate = reply.MessageTemplate,
        IsActive = reply.IsActive,
        UsageCount = reply.UsageCount,
        LastUsedAt = reply.LastUsedAt,
        CreatedAt = reply.CreatedAt,
        UpdatedAt = reply.UpdatedAt
    };

    private Task<bool> IsBranchAiActiveAsync(int branchId, CancellationToken ct) => _db.BranchAiSettings.AsNoTracking().AnyAsync(x => x.BranchId == branchId && x.IsActive && x.IsVerified, ct);

    private async Task<WhatsAppAttentionDto> ToAttentionDtoAsync(WhatsAppConversation conversation, CancellationToken ct)
    {
        var name = conversation.AssignedUserId.HasValue ? await _db.Users.AsNoTracking().Where(x => x.Id == conversation.AssignedUserId).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var reason = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman
            ? await _db.WhatsAppMessages.AsNoTracking()
                .Where(x => x.ConversationId == conversation.Id
                    && x.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman
                    && x.AiProcessingError != null)
                .OrderByDescending(x => x.Timestamp)
                .ThenByDescending(x => x.Id)
                .Select(x => x.AiProcessingError)
                .FirstOrDefaultAsync(ct)
            : null;
        return new WhatsAppAttentionDto { ConversationId = conversation.Id, AttentionMode = AttentionModeToApi(conversation.AttentionMode), AttentionReason = WhatsAppAiDiagnosticsMapper.SanitizeTechnicalDetail(reason), AssignedUserId = conversation.AssignedUserId, AssignedUserName = name, AiPausedAt = AsUtc(conversation.AiPausedAt), HumanAssignedAt = AsUtc(conversation.HumanAssignedAt), ClosedAt = AsUtc(conversation.ClosedAt), AttentionModeUpdatedAt = AsUtc(conversation.AttentionModeUpdatedAt), AttentionModeUpdatedByUserId = conversation.AttentionModeUpdatedByUserId };
    }

    private static string AttentionModeToApi(WhatsAppAttentionMode mode) => mode switch { WhatsAppAttentionMode.Ai => "ai", WhatsAppAttentionMode.Human => "human", WhatsAppAttentionMode.WaitingForHuman => "waitingForHuman", WhatsAppAttentionMode.Paused => "paused", WhatsAppAttentionMode.Closed => "closed", _ => "human" };

    private static WhatsAppConversationDto ToConversationDto(WhatsAppConversation conversation, string? assignedUserName = null, string? attentionReason = null) => new()
    {
        Id = conversation.Id,
        BranchId = conversation.BranchId,
        BranchName = conversation.Branch?.Name,
        CustomerId = conversation.CustomerId,
        CustomerName = conversation.Customer?.Name,
        PhoneNumber = conversation.PhoneNumber,
        ContactName = conversation.ContactName,
        Status = ConversationStatusToApi(conversation.Status),
        LastMessageAt = AsUtc(conversation.LastMessageAt),
        LastMessagePreview = conversation.LastMessagePreview,
        UnreadCount = conversation.UnreadCount,
        AttentionMode = AttentionModeToApi(conversation.AttentionMode),
        AttentionReason = conversation.AttentionMode == WhatsAppAttentionMode.WaitingForHuman ? attentionReason : null,
        AssignedUserId = conversation.AssignedUserId,
        AssignedUserName = assignedUserName,
        AiPausedAt = AsUtc(conversation.AiPausedAt),
        HumanAssignedAt = AsUtc(conversation.HumanAssignedAt),
        ClosedAt = AsUtc(conversation.ClosedAt),
        AttentionModeUpdatedAt = AsUtc(conversation.AttentionModeUpdatedAt),
        AttentionModeUpdatedByUserId = conversation.AttentionModeUpdatedByUserId,
        CreatedAt = AsUtc(conversation.CreatedAt),
        UpdatedAt = AsUtc(conversation.UpdatedAt)
    };

    private WhatsAppMessageDto ToMessageDto(WhatsAppMessage message) => new()
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
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private static string ConversationStatusToApi(WhatsAppConversationStatus status) => status switch
    {
        WhatsAppConversationStatus.Open => "open",
        WhatsAppConversationStatus.Pending => "pending",
        WhatsAppConversationStatus.Closed => "closed",
        WhatsAppConversationStatus.Archived => "archived",
        _ => "open"
    };

    private static string MessageStatusToApi(WhatsAppMessageStatus status) => status switch
    {
        WhatsAppMessageStatus.Received => "received",
        WhatsAppMessageStatus.Sent => "sent",
        WhatsAppMessageStatus.Delivered => "delivered",
        WhatsAppMessageStatus.Read => "read",
        WhatsAppMessageStatus.Failed => "failed",
        _ => "received"
    };

    private static string MediaTypeToApi(WhatsAppMessageType type) => type switch
    {
        WhatsAppMessageType.Text => "text",
        WhatsAppMessageType.Image => "image",
        WhatsAppMessageType.Audio => "audio",
        WhatsAppMessageType.Video => "video",
        WhatsAppMessageType.Document => "document",
        WhatsAppMessageType.Sticker => "sticker",
        _ => "document"
    };

    private static WhatsAppMessageType MediaTypeFromContentType(string? contentType, string? fileName)
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

    private static string PreviewForMedia(WhatsAppMessageType type, string text)
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

    private static string SanitizeFileName(string? fileName)
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

    private readonly record struct InboundMediaPayload(
        string MediaId,
        string? MimeType,
        string? Sha256,
        string? FileName,
        string? Caption);

    private readonly record struct StoredWhatsAppMedia(
        string MediaId,
        string? MediaUrl,
        string? MimeType,
        string? FileName,
        long? FileSize,
        string? Sha256);

    private sealed record TemplateCredentialsResolution(
        bool Forbidden,
        string? ErrorMessage,
        int? BranchId,
        string AccessToken,
        string? BusinessAccountId,
        string? PhoneNumberId)
    {
        public static TemplateCredentialsResolution Denied() => new(true, null, null, string.Empty, null, null);
        public static TemplateCredentialsResolution Failed(string errorMessage) => new(false, errorMessage, null, string.Empty, null, null);
    }

    private sealed record TemplateRecipient(string PhoneNumber, Customer? Customer);

    private sealed record TemplateRecipientsResolution(
        bool Forbidden,
        string? ErrorMessage,
        IReadOnlyList<TemplateRecipient> Recipients)
    {
        public static TemplateRecipientsResolution Denied() => new(true, null, []);
        public static TemplateRecipientsResolution Failed(string errorMessage) => new(false, errorMessage, []);
    }
}
