using System.Text.Json;
using static SenorArroz.API.Services.WhatsAppWebhookProcessor;
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

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController : ControllerBase
{
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
    private readonly WhatsAppCommerceFlowService _commerceFlow;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ITenantExecutionContext? _tenantExecutionContext;
    private readonly IInventoryService? _inventory;
    private int TenantId => _currentTenant?.TenantId ?? _commerceFlow.TenantId;
    private readonly int _aiMaxPersistentAttempts;

    private readonly WhatsAppWebhookProcessor _webhookProcessor;
    private readonly WhatsAppProcessingStore? _processingStore;
    private readonly WhatsAppProcessingOptions _processingOptions;

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
        WhatsAppAwayMessageService awayMessageService,
        WhatsAppCommerceFlowService commerceFlow,
        ICurrentTenant? currentTenant = null,
        ITenantExecutionContext? tenantExecutionContext = null,
        IInventoryService? inventory = null,
        WhatsAppWebhookProcessor? webhookProcessor = null,
        WhatsAppProcessingStore? processingStore = null,
        IOptions<WhatsAppProcessingOptions>? processingOptions = null)
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
        _commerceFlow = commerceFlow;
        _currentTenant = currentTenant;
        _tenantExecutionContext = tenantExecutionContext;
        _inventory = inventory;
        _processingStore = processingStore;
        _processingOptions = processingOptions?.Value ?? new();
        _webhookProcessor = webhookProcessor ?? new WhatsAppWebhookProcessor(db, clock, whatsAppCloudClient, whatsAppNotificationService, firebaseStorage, firebaseOptions,
            aiOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppWebhookProcessor>.Instance,
            attentionService, aiWorkQueue, automaticMessageSender, businessHoursService, awayMessageService,
            commerceFlow, currentTenant, inventory);
    }

    [HttpGet("status")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppStatusDto>>> GetStatus(CancellationToken cancellationToken)
    {
        var branchIds = await GetAllowedVerifiedBranchIdsQuery()
            .ToListAsync(cancellationToken);
        var centralEnabled = await _db.WhatsAppChannelSettings.AsNoTracking()
            .AnyAsync(x => x.TenantId == TenantId && x.IsActive && x.IsVerified, cancellationToken);

        return Ok(ApiResponse<WhatsAppStatusDto>.SuccessResponse(new WhatsAppStatusDto
        {
            Enabled = branchIds.Count > 0 || centralEnabled,
            BranchIds = branchIds
        }, "Estado de WhatsApp obtenido."));
    }

    [HttpGet("operational-branches")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppOperationalBranchOptionDto>>>> GetOperationalBranches(CancellationToken cancellationToken)
    {
        var branches = await _db.Branches.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new WhatsAppOperationalBranchOptionDto { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<WhatsAppOperationalBranchOptionDto>>.SuccessResponse(branches, "Sucursales operativas obtenidas."));
    }

    [HttpGet("unread-summary")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppUnreadSummaryDto>>> GetUnreadSummary(CancellationToken cancellationToken)
    {
        var query = GetAccessibleConversationsQuery().Where(x => x.UnreadCount > 0);

        // Aggregate unread counts and latest timestamp in a single SQL query.
        var summary = await query
            .GroupBy(_ => 1)
            .Select(group => new WhatsAppUnreadSummaryDto
            {
                TotalUnread = group.Sum(x => x.UnreadCount),
                UnreadConversations = group.Count(),
                LatestMessageAt = group.Max(x => x.LastMessageAt)
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? new WhatsAppUnreadSummaryDto();

        return Ok(ApiResponse<WhatsAppUnreadSummaryDto>.SuccessResponse(
            summary, "Resumen de WhatsApp obtenido."));
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

        var requiresPhone = string.Equals(template.Category, "AUTHENTICATION", StringComparison.OrdinalIgnoreCase);
        var recipients = await ResolveTemplateRecipientsAsync(dto, credentials.BranchId, requiresPhone, cancellationToken);
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
                recipient.Recipient,
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
                    await PersistOutboundTemplateMessageAsync(credentials.BranchId.Value, recipient.Recipient, recipient.Customer, template, parameters, result.WhatsAppMessageId, cancellationToken);
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add($"{FormatRecipientForError(recipient.Recipient)}: {result.ErrorMessage ?? "Meta rechazó el envío."}");
                _logger.LogWarning("WhatsApp template send failed. Template={Template} To={To} Error={Error}", template.Name, recipient.Recipient, result.ErrorMessage);
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

        bool exists;
        using (_tenantExecutionContext?.BeginSystemScope())
        {
            exists = await _db.WhatsAppChannelSettings.AsNoTracking()
                .AnyAsync(x => x.WebhookVerifyToken == verifyToken, cancellationToken)
                || await _db.WhatsAppBranchSettings.AsNoTracking()
                    .AnyAsync(x => x.WebhookVerifyToken == verifyToken, cancellationToken);
        }

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
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync(cancellationToken);
        JsonDocument document;
        try { document = JsonDocument.Parse(rawPayload); }
        catch (JsonException) { return BadRequest(); }
        using (document)
        {
            int? tenantId;
            using (_tenantExecutionContext?.BeginSystemScope())
                tenantId = await ResolveWebhookTenantIdAsync(document.RootElement, cancellationToken);
            if (!tenantId.HasValue) return Unauthorized();
            using var tenantScope = _tenantExecutionContext?.BeginTenantScope(tenantId.Value);
            if (!await ValidateWebhookSignatureAsync(document.RootElement, rawPayload, cancellationToken))
                return Unauthorized();
            if (_processingOptions.External)
            {
                await _processingStore!.EnqueueAsync(tenantId.Value, rawPayload, cancellationToken);
                return Ok();
            }
            var webhookEvent = new WhatsAppWebhookEvent
            {
                TenantId = tenantId.Value, EventType = "webhook",
                RawPayload = WhatsAppFlowPayload.WithoutTokens(rawPayload)
            };
            _db.WhatsAppWebhookEvents.Add(webhookEvent);
            await _webhookProcessor.ProcessAsync(webhookEvent, rawPayload, cancellationToken);
            return Ok();
        }
    }

    [HttpGet("conversations")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppConversationDto>>>> GetConversations(
        [FromQuery] WhatsAppConversationSearchDto search,
        CancellationToken cancellationToken)
    {
        var accessibleConversations = GetAccessibleConversationsQuery();
        var isSuperadmin = Roles.IsSuperadmin(_currentUser.Role);
        var adminBranchId = _currentUser.BranchId;
        if (search.BranchId.HasValue && !isSuperadmin && search.BranchId != adminBranchId)
            return Forbid();

        IQueryable<WhatsAppConversation> query = accessibleConversations
            .Include(x => x.Branch)
            .Include(x => x.OperationalBranch)
            .Include(x => x.Customer);

        if (search.BranchId.HasValue)
            query = query.Where(x => x.ChannelSettingId == null
                ? x.BranchId == search.BranchId.Value
                : x.OperationalBranchId == search.BranchId.Value);

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim().ToLowerInvariant();
            var usernameTerm = WhatsAppIdentityNormalizer.NormalizeUsername(term);
            query = query.Where(x =>
                (x.PhoneNumber != null && x.PhoneNumber.ToLower().Contains(term))
                || (x.WhatsAppUsername != null && (x.WhatsAppUsername.ToLower().Contains(term)
                    || (usernameTerm != null && x.WhatsAppUsername.ToLower().Contains(usernameTerm))))
                || (x.ContactName != null && x.ContactName.ToLower().Contains(term))
                || (x.Customer != null && (x.Customer.Name.ToLower().Contains(term)
                    || (x.Customer.Phone1 != null && x.Customer.Phone1.Contains(term))
                    || (x.Customer.Phone2 != null && x.Customer.Phone2.Contains(term))
                    || (x.Customer.WhatsAppUsername != null && (x.Customer.WhatsAppUsername.ToLower().Contains(term)
                        || (usernameTerm != null && x.Customer.WhatsAppUsername.ToLower().Contains(usernameTerm)))))));
        }

        if (!string.IsNullOrWhiteSpace(search.Status) && TryParseConversationStatus(search.Status, out var status))
            query = query.Where(x => x.Status == status);

        if (search.UnreadOnly == true)
            query = query.Where(x => x.UnreadCount > 0);

        var conversationEntities = await query
            .OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        var assignedIds = conversationEntities.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value).Distinct().ToList();
        var assignedNames = assignedIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking().Where(x => assignedIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
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

    [HttpPut("conversations/{conversationId:int}/operational-branch")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppConversationDto>>> UpdateOperationalBranch(
        int conversationId,
        [FromBody] UpdateWhatsAppOperationalBranchDto request,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations
            .Include(x => x.OperationalBranch)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppConversationDto>.ErrorResponse("Conversación no encontrada."));
        if (!conversation.ChannelSettingId.HasValue || !await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();

        Branch? operationalBranch = null;
        if (request.OperationalBranchId.HasValue)
        {
            operationalBranch = await _db.Branches
                .FirstOrDefaultAsync(x => x.Id == request.OperationalBranchId.Value && x.IsActive, cancellationToken);
            if (operationalBranch is null)
                return BadRequest(ApiResponse<WhatsAppConversationDto>.ErrorResponse("La sucursal operativa no existe o está inactiva."));
        }

        var previousOperationalBranchId = conversation.OperationalBranchId;
        var previousAssignedUserId = conversation.AssignedUserId;
        if (previousOperationalBranchId == request.OperationalBranchId)
            return Ok(ApiResponse<WhatsAppConversationDto>.SuccessResponse(ToConversationDto(conversation), "La sucursal operativa no cambió."));

        conversation.OperationalBranchId = request.OperationalBranchId;
        conversation.OperationalBranch = operationalBranch;
        if (previousOperationalBranchId.HasValue && request.OperationalBranchId.HasValue)
            conversation.AssignedUserId = null;
        conversation.UpdatedAt = _clock.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(ApiResponse<WhatsAppConversationDto>.ErrorResponse("La conversación fue modificada por otro usuario. Actualiza e inténtalo de nuevo."));
        }

        var dto = ToConversationDto(conversation);
        await _whatsAppNotificationService.NotifyConversationRoutingChangedAsync(
            conversation.BranchId,
            previousOperationalBranchId,
            previousAssignedUserId,
            dto,
            cancellationToken);

        return Ok(ApiResponse<WhatsAppConversationDto>.SuccessResponse(dto, "Sucursal operativa actualizada."));
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
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

    [HttpGet("conversations/{conversationId:int}/messages/page")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessagePageDto>>> GetMessagesPage(
        int conversationId, [FromQuery] int limit = 50, [FromQuery] string? before = null, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.WhatsAppConversations.FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
        if (limit is < 1 or > 100) return BadRequest("El tamaño de página debe estar entre 1 y 100.");
        var query = _db.WhatsAppMessages.Where(x => x.ConversationId == conversationId);
        if (!string.IsNullOrWhiteSpace(before))
        {
            try
            {
                var encoded = before.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[0], out var owner) || owner != conversationId
                    || !long.TryParse(parts[1], out var ticks) || !int.TryParse(parts[2], out var id) || id <= 0)
                    return BadRequest("Cursor inválido.");
                var timestamp = new DateTime(ticks, DateTimeKind.Utc);
                query = query.Where(x => x.Timestamp < timestamp || x.Timestamp == timestamp && x.Id < id);
            }
            catch (Exception error) when (error is FormatException or ArgumentOutOfRangeException)
            {
                return BadRequest("Cursor inválido.");
            }
        }
        else if (conversation.UnreadCount > 0)
        {
            conversation.UnreadCount = 0;
            await _db.SaveChangesAsync(cancellationToken);
        }
        var messages = await query.OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id)
            .Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = messages.Count > limit;
        if (hasMore) messages.RemoveAt(messages.Count - 1);
        messages.Reverse();
        await EnsureMediaDownloadUrlsAsync(messages, cancellationToken);
        var oldest = messages.FirstOrDefault();
        var cursor = hasMore && oldest is not null
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{conversationId}:{oldest.Timestamp.Ticks}:{oldest.Id}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_') : null;
        return Ok(ApiResponse<WhatsAppMessagePageDto>.SuccessResponse(new(messages.Select(ToMessageDto).ToList(), cursor, hasMore)));
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();

        var operationalBranchId = conversation.OperationalBranchId ?? conversation.BranchId;
        var state = await orderState.LoadAsync(conversationId, cancellationToken);
        var summary = await orderState.BuildSummaryAsync(operationalBranchId, state, cancellationToken);
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
                    x.Neighborhood != null ? x.Neighborhood.Name : null,
                    x.DeliveryFee))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var deliveryFee = state.OrderType == OrderType.Delivery ? selectedAddress?.DeliveryFee ?? 0 : 0;
        var notes = state.Items.ToDictionary(x => x.ProductId, x => x.Notes);
        var dto = new WhatsAppOrderDraftDto
        {
            ConversationId = conversation.Id,
            BranchId = operationalBranchId,
            CustomerId = conversation.CustomerId,
            CustomerName = conversation.Customer?.Name ?? conversation.ContactName,
            PhoneNumber = conversation.PhoneNumber,
            WhatsAppUsername = conversation.WhatsAppUsername,
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
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
                    : $"Un asesor seleccionó la dirección {address.AddressText}{(address.Neighborhood == null ? string.Empty : $", {address.Neighborhood.Name}")}.",
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();

        var customer = await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == dto.CustomerId && x.BranchId == conversation.BranchId && x.Active, cancellationToken);
        if (customer is null)
            return BadRequest(ApiResponse<WhatsAppConversationDto>.ErrorResponse("Cliente no encontrado para la sucursal de esta conversación."));

        if (!string.IsNullOrWhiteSpace(conversation.WhatsAppUserId)
            && !string.IsNullOrWhiteSpace(customer.WhatsAppUserId)
            && !string.Equals(conversation.WhatsAppUserId, customer.WhatsAppUserId, StringComparison.Ordinal))
        {
            return Conflict(ApiResponse<WhatsAppConversationDto>.ErrorResponse("El cliente ya está asociado a otra identidad de WhatsApp."));
        }

        if (!string.IsNullOrWhiteSpace(conversation.WhatsAppUserId))
        {
            var previousCustomer = conversation.Customer;
            var transferablePreviousCustomerId = previousCustomer is not null
                && previousCustomer.Id != customer.Id
                && string.Equals(previousCustomer.WhatsAppUserId, conversation.WhatsAppUserId, StringComparison.Ordinal)
                    ? previousCustomer.Id
                    : (int?)null;
            var conflictingCustomer = await _db.Customers.AsNoTracking().AnyAsync(x =>
                x.TenantId == conversation.TenantId
                && x.Id != customer.Id
                && (!transferablePreviousCustomerId.HasValue || x.Id != transferablePreviousCustomerId.Value)
                && x.WhatsAppUserId == conversation.WhatsAppUserId,
                cancellationToken);
            if (conflictingCustomer)
                return Conflict(ApiResponse<WhatsAppConversationDto>.ErrorResponse("La identidad de WhatsApp ya pertenece a otro cliente de la sucursal."));

            if (transferablePreviousCustomerId.HasValue)
                previousCustomer!.WhatsAppUserId = null;

            customer.WhatsAppUserId = conversation.WhatsAppUserId;
            customer.WhatsAppUsername = conversation.WhatsAppUsername ?? customer.WhatsAppUsername;
        }

        if (string.IsNullOrWhiteSpace(customer.Phone1) && !string.IsNullOrWhiteSpace(conversation.PhoneNumber))
        {
            var customerPhone = NormalizeCustomerPhone(conversation.PhoneNumber);
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
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
        var aiActive = conversation.ChannelSettingId.HasValue
            ? await _db.TenantAiSettings.AsNoTracking().AnyAsync(x => x.TenantId == conversation.TenantId && x.IsActive && x.IsVerified, cancellationToken)
            : await IsBranchAiActiveAsync(conversation.BranchId, cancellationToken);
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
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var aiActive = conversation.ChannelSettingId.HasValue
            ? await _db.TenantAiSettings.AsNoTracking().AnyAsync(x => x.TenantId == conversation.TenantId && x.IsActive && x.IsVerified, ct)
            : await IsBranchAiActiveAsync(conversation.BranchId, ct);
        var now = _clock.UtcNow; var userId = _currentUser.Id;
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();
        if (conversation.AttentionMode == WhatsAppAttentionMode.Closed)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación está cerrada. Debes reabrirla antes de enviar mensajes."));

        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene teléfono ni identidad de WhatsApp para responder."));

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
            recipient,
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
                to = recipient,
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
        var product = await _db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.CommercialProfile).FirstOrDefaultAsync(x => x.Id == productId && x.Category.BranchId == conversation.BranchId, cancellationToken);
        if (product is null) return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Producto no encontrado."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));
        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken)
            ?? throw new InvalidOperationException("WhatsApp no está activo y verificado.");
        var serves = product.ServesPeopleMin == product.ServesPeopleMax && product.ServesPeopleMin.HasValue ? $"{product.ServesPeopleMin} {(product.ServesPeopleMin == 1 ? "persona" : "personas")}" : product.ServesPeopleMin.HasValue ? $"{product.ServesPeopleMin}-{product.ServesPeopleMax} personas" : null;
        var inventoryAvailability = _inventory is null ? null : (await _inventory.GetAvailabilityAsync([product.Id], conversation.BranchId, cancellationToken)).SingleOrDefault();
        var available = _inventory is null ? product.Active : inventoryAvailability?.Available == true;
        var text = string.Join("\n", new[] { product.Name, product.CommercialProfile?.Description, string.IsNullOrWhiteSpace(product.CommercialProfile?.Ingredients) ? null : $"Ingredientes: {product.CommercialProfile.Ingredients}", serves is null ? null : $"Rinde para {serves}", $"Precio: ${product.Price:N0}", available ? "Disponible" : "No disponible" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var result = !string.IsNullOrWhiteSpace(product.CommercialProfile?.PhotoUrl)
            ? await _whatsAppCloudClient.SendImageLinkMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, product.CommercialProfile.PhotoUrl, text, cancellationToken)
            : await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, text, cancellationToken);
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
        var branch = await _db.Branches.AsNoTracking().FirstAsync(x => x.Id == conversation.BranchId, cancellationToken);
        if (string.IsNullOrWhiteSpace(branch.MenuImageUrl1) && string.IsNullOrWhiteSpace(branch.MenuImageUrl2))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Esta sucursal todavía no tiene una carta configurada."));
        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));
        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/public/menu?branchId={conversation.BranchId}";
        var text = $"Consulta nuestra carta aquí: {url}";
        var menuImages = new[] { branch.MenuImageUrl1, branch.MenuImageUrl2 }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
        var sentMessages = new List<WhatsAppMessage>();
        string? lastError = null;
        for (var index = 0; index < menuImages.Count; index++)
        {
            var caption = index == 0 ? "Nuestra carta actual" : "Carta (continuación)";
            var result = await _whatsAppCloudClient.SendImageLinkMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, menuImages[index], caption, cancellationToken);
            if (!result.Success) { lastError = result.ErrorMessage; break; }
            var timestamp = _clock.UtcNow;
            var message = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = result.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = WhatsAppMessageType.Image, TextBody = caption, MediaUrl = menuImages[index], Status = WhatsAppMessageStatus.Sent, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, Timestamp = timestamp, RawPayload = JsonSerializer.Serialize(new { action = "send_menu", mode = "image", slot = index + 1, result.Success }) };
            _db.WhatsAppMessages.Add(message); sentMessages.Add(message);
        }
        var usedFallback = sentMessages.Count != menuImages.Count;
        if (usedFallback)
        {
            var fallback = await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, text, cancellationToken);
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
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

        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));

        var timestamp = _clock.UtcNow;
        var result = await _whatsAppCloudClient.SendTextMessageAsync(
            setting.PhoneNumberId,
            setting.AccessToken,
            recipient,
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
                to = recipient,
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
        if (!await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();

        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));

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
                recipient,
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
                to = recipient,
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

    private IQueryable<WhatsAppConversation> GetAccessibleConversationsQuery()
    {
        var branchIds = GetAllowedVerifiedBranchIdsQuery();
        var tenantId = TenantId;
        var userId = _currentUser.Id;
        var branchId = _currentUser.BranchId;
        var isSuperadmin = Roles.IsSuperadmin(_currentUser.Role);
        var isBranchInboxUser = Roles.IsAdminOrCashier(_currentUser.Role);
        var channelIds = _db.WhatsAppChannelSettings.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.IsVerified)
            .Select(x => x.Id);

        return _db.WhatsAppConversations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && (
                (x.ChannelSettingId == null && branchIds.Contains(x.BranchId))
                || (x.ChannelSettingId.HasValue && channelIds.Contains(x.ChannelSettingId.Value)
                    && (isSuperadmin || isBranchInboxUser && (x.OperationalBranchId == null
                        || x.OperationalBranchId == branchId || x.AssignedUserId == userId)))));
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

    private async Task<bool> CanAccessConversationAsync(WhatsAppConversation conversation, CancellationToken cancellationToken)
    {
        if (!conversation.ChannelSettingId.HasValue)
            return await CanAccessVerifiedBranchAsync(conversation.BranchId, cancellationToken);
        var channelReady = await _db.WhatsAppChannelSettings.AsNoTracking().AnyAsync(
            x => x.Id == conversation.ChannelSettingId.Value && x.TenantId == TenantId && x.IsActive && x.IsVerified,
            cancellationToken);
        if (!channelReady) return false;
        if (Roles.IsSuperadmin(_currentUser.Role)) return true;
        if (!conversation.OperationalBranchId.HasValue)
            return Roles.IsAdminOrCashier(_currentUser.Role);
        return conversation.OperationalBranchId == _currentUser.BranchId
            || Roles.IsAdminOrCashier(_currentUser.Role) && conversation.AssignedUserId == _currentUser.Id;
    }

    private async Task<ConversationChannelCredentials?> ResolveConversationChannelAsync(
        WhatsAppConversation conversation,
        CancellationToken cancellationToken)
    {
        if (conversation.ChannelSettingId.HasValue)
        {
            return await _db.WhatsAppChannelSettings.AsNoTracking()
                .Where(x => x.Id == conversation.ChannelSettingId.Value && x.TenantId == TenantId && x.IsActive && x.IsVerified)
                .Select(x => new ConversationChannelCredentials(x.PhoneNumberId, x.AccessToken))
                .FirstOrDefaultAsync(cancellationToken);
        }
        return await _db.WhatsAppBranchSettings.AsNoTracking()
            .Where(x => x.BranchId == conversation.BranchId && x.IsActive && x.IsVerified)
            .Select(x => new ConversationChannelCredentials(x.PhoneNumberId, x.AccessToken))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record ConversationChannelCredentials(string PhoneNumberId, string AccessToken);

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
        bool requiresPhone,
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
                if (IsInternationalWhatsAppPhone(phone))
                {
                    recipients.Add(new TemplateRecipient(phone, customer));
                    continue;
                }

                if (!requiresPhone && !string.IsNullOrWhiteSpace(customer.WhatsAppUserId))
                    recipients.Add(new TemplateRecipient(customer.WhatsAppUserId, customer));
            }
        }

        var unique = recipients
            .GroupBy(x => x.Recipient)
            .Select(x => x.First())
            .ToList();

        if (unique.Count == 0)
            return TemplateRecipientsResolution.Failed(requiresPhone
                ? "Las plantillas de autenticación requieren un teléfono internacional válido."
                : "Debe indicar al menos un número válido o seleccionar clientes con identidad de WhatsApp.");

        return new TemplateRecipientsResolution(false, null, unique);
    }

    private async Task PersistOutboundTemplateMessageAsync(
        int branchId,
        string recipient,
        Customer? customer,
        WhatsAppTemplate template,
        IReadOnlyList<string> parameters,
        string? whatsAppMessageId,
        CancellationToken cancellationToken)
    {
        var timestamp = _clock.UtcNow;
        var userId = string.Equals(customer?.WhatsAppUserId, recipient, StringComparison.Ordinal)
            ? recipient
            : null;
        var phoneNumber = userId is null ? recipient : null;
        var conversation = await _db.WhatsAppConversations
            .FirstOrDefaultAsync(x => x.BranchId == branchId
                && (userId != null ? x.WhatsAppUserId == userId : x.PhoneNumber == phoneNumber),
                cancellationToken);

        if (conversation is null)
        {
            var aiActive = await IsBranchAiActiveAsync(branchId, cancellationToken);
            conversation = new WhatsAppConversation
            {
                BranchId = branchId,
                PhoneNumber = phoneNumber,
                WhatsAppUserId = userId,
                WhatsAppUsername = customer?.WhatsAppUsername,
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
        conversation.WhatsAppUsername = customer?.WhatsAppUsername ?? conversation.WhatsAppUsername;
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

    private static string FormatRecipientForError(string recipient) =>
        IsInternationalWhatsAppPhone(recipient) ? $"+{recipient}" : "usuario de WhatsApp";

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
            ["telefono"] = conversation.PhoneNumber ?? string.Empty,
            ["usuario_whatsapp"] = conversation.WhatsAppUsername ?? string.Empty,
        };

        return Regex.Replace(template, @"{{\s*([a-zA-Z0-9_]+)\s*}}", match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var replacement) ? replacement : match.Value;
        });
    }

    private async Task<bool> ValidateWebhookSignatureAsync(JsonElement payload, string rawPayload, CancellationToken cancellationToken)
    {
        var phoneNumberIds = new HashSet<string>(StringComparer.Ordinal);
        if (payload.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var entry in entries.EnumerateArray())
                if (entry.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                    foreach (var change in changes.EnumerateArray())
                        if (change.TryGetProperty("value", out var value) && TryGetPhoneNumberId(value) is { Length: > 0 } phoneNumberId)
                            phoneNumberIds.Add(phoneNumberId);
        if (phoneNumberIds.Count == 0) return false;
        var channelSecrets = await _db.WhatsAppChannelSettings.AsNoTracking()
            .Where(x => phoneNumberIds.Contains(x.PhoneNumberId) && x.IsActive && x.AppSecret != null)
            .Select(x => x.AppSecret!)
            .ToListAsync(cancellationToken);
        var branchSecrets = await _db.WhatsAppBranchSettings.AsNoTracking()
            .Where(x => phoneNumberIds.Contains(x.PhoneNumberId) && x.IsActive && x.AppSecret != null)
            .Select(x => x.AppSecret!)
            .ToListAsync(cancellationToken);
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        return channelSecrets.Concat(branchSecrets).Distinct(StringComparer.Ordinal)
            .Any(secret => WhatsAppWebhookSignature.IsValid(signature, rawPayload, secret));
    }

    private async Task<int?> ResolveWebhookTenantIdAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var phoneNumberIds = new HashSet<string>(StringComparer.Ordinal);
        if (payload.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var entry in entries.EnumerateArray())
                if (entry.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                    foreach (var change in changes.EnumerateArray())
                        if (change.TryGetProperty("value", out var value)
                            && TryGetPhoneNumberId(value) is { Length: > 0 } phoneNumberId)
                            phoneNumberIds.Add(phoneNumberId);
        if (phoneNumberIds.Count == 0)
            return null;

        var centralTenantIds = await _db.WhatsAppChannelSettings.AsNoTracking()
            .Where(x => phoneNumberIds.Contains(x.PhoneNumberId) && x.IsActive && x.IsVerified)
            .Select(x => x.TenantId)
            .ToListAsync(cancellationToken);
        var branchTenantIds = await _db.WhatsAppBranchSettings.AsNoTracking()
            .Where(x => phoneNumberIds.Contains(x.PhoneNumberId) && x.IsActive && x.IsVerified)
            .Select(x => x.TenantId)
            .ToListAsync(cancellationToken);
        var tenantIds = centralTenantIds.Concat(branchTenantIds).Distinct().Take(2).ToList();
        return tenantIds.Count == 1 ? tenantIds[0] : null;
    }

    private static string NormalizeWhatsAppPhone(string? value) => OnlyDigits(value);

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

    private static string ConversationStatusToApi(WhatsAppConversationStatus status) => status switch
    {
        WhatsAppConversationStatus.Open => "open",
        WhatsAppConversationStatus.Pending => "pending",
        WhatsAppConversationStatus.Closed => "closed",
        WhatsAppConversationStatus.Archived => "archived",
        _ => "open"
    };

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

    private sealed record TemplateRecipient(string Recipient, Customer? Customer);

    private sealed record TemplateRecipientsResolution(
        bool Forbidden,
        string? ErrorMessage,
        IReadOnlyList<TemplateRecipient> Recipients)
    {
        public static TemplateRecipientsResolution Denied() => new(true, null, []);
        public static TemplateRecipientsResolution Failed(string errorMessage) => new(false, errorMessage, []);
    }
    private Task NotifyWhatsAppMessageCreatedAsync(int messageId, CancellationToken cancellationToken) => _webhookProcessor.NotifyWhatsAppMessageCreatedAsync(messageId, cancellationToken);

    private static string? TryGetPhoneNumberId(JsonElement value) => WhatsAppWebhookProcessor.TryGetPhoneNumberId(value);

    private Task<string> SaveWhatsAppMediaToFirebaseAsync(
        int branchId,
        int conversationId,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken) => _webhookProcessor.SaveWhatsAppMediaToFirebaseAsync(branchId, conversationId, content, fileName, contentType, cancellationToken);

    private static string? NormalizeCustomerPhone(string? value) => WhatsAppWebhookProcessor.NormalizeCustomerPhone(value);

    private Task<bool> CanAssignPhoneToCustomerAsync(
        int customerId,
        string phone,
        CancellationToken cancellationToken) => _webhookProcessor.CanAssignPhoneToCustomerAsync(customerId, phone, cancellationToken);

    private static string? TryGetString(JsonElement element, string propertyName) => WhatsAppWebhookProcessor.TryGetString(element, propertyName);

    private static string OnlyDigits(string? value) => WhatsAppWebhookProcessor.OnlyDigits(value);

    private Task<bool> IsBranchAiActiveAsync(int branchId, CancellationToken ct) => _webhookProcessor.IsBranchAiActiveAsync(branchId, ct);

    private static WhatsAppConversationDto ToConversationDto(WhatsAppConversation conversation, string? assignedUserName = null, string? attentionReason = null) => WhatsAppWebhookProcessor.ToConversationDto(conversation, assignedUserName, attentionReason);

    private WhatsAppMessageDto ToMessageDto(WhatsAppMessage message) => _webhookProcessor.ToMessageDto(message);

    private static DateTime AsUtc(DateTime value) => WhatsAppWebhookProcessor.AsUtc(value);

    private static DateTime? AsUtc(DateTime? value) => WhatsAppWebhookProcessor.AsUtc(value);

    private static string MediaTypeToApi(WhatsAppMessageType type) => WhatsAppWebhookProcessor.MediaTypeToApi(type);

    private static WhatsAppMessageType MediaTypeFromContentType(string? contentType, string? fileName) => WhatsAppWebhookProcessor.MediaTypeFromContentType(contentType, fileName);

    private static string PreviewForMedia(WhatsAppMessageType type, string text) => WhatsAppWebhookProcessor.PreviewForMedia(type, text);

    private static string SanitizeFileName(string? fileName) => WhatsAppWebhookProcessor.SanitizeFileName(fileName);


}
