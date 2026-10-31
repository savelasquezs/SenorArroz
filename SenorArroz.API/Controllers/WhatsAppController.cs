using System.Text.Json;
using static SenorArroz.API.Services.WhatsAppWebhookProcessor;
using static SenorArroz.API.Services.WhatsAppManualOperations;
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

    private readonly WhatsAppManualOperations _manualOperations;
    private readonly WhatsAppManualWorkQueue? _manualQueue;
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
        IOptions<WhatsAppProcessingOptions>? processingOptions = null,
        WhatsAppManualOperations? manualOperations = null,
        WhatsAppManualWorkQueue? manualQueue = null)
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
        _manualOperations = manualOperations ?? new WhatsAppManualOperations(db, currentUser, branchContext, clock,
            whatsAppCloudClient, whatsAppNotificationService, whatsAppOptions, logger, attentionService,
            commerceFlow, _webhookProcessor, currentTenant, inventory, processingOptions);
        _manualQueue = manualQueue;
    }

    [HttpGet("operations/{operationId:int}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetManualOperation(int operationId, CancellationToken ct) =>
        _manualQueue is null ? NotFound() : await _manualQueue.GetResultAsync(operationId, ct);

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
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppTemplateSyncResultDto>>> SyncTemplates(
        [FromBody] SyncWhatsAppTemplatesDto? dto,
        CancellationToken cancellationToken)
    {

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppTemplateSyncResultDto>("sync-templates", null, JsonSerializer.SerializeToElement(dto ?? new SyncWhatsAppTemplatesDto()), Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SyncTemplates(dto, cancellationToken);
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
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppTemplateSendResultDto>>> SendTemplate(
        [FromBody] SendWhatsAppTemplateDto dto,
        CancellationToken cancellationToken)
    {

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppTemplateSendResultDto>("send-template", null, JsonSerializer.SerializeToElement(dto), Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendTemplate(dto, cancellationToken);
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
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMessage(
        int conversationId,
        [FromBody] SendWhatsAppMessageDto dto,
        CancellationToken cancellationToken)
    {

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppMessageDto>("send-message", conversationId, JsonSerializer.SerializeToElement(dto), Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendMessage(conversationId, dto, cancellationToken);
    }

    [HttpPost("conversations/{conversationId:int}/messages/products/{productId:int}")]
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendProductDetails(int conversationId, int productId, CancellationToken cancellationToken)
    {

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppMessageDto>("send-product", conversationId, JsonSerializer.SerializeToElement(new { productId }), Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendProductDetails(conversationId, productId, cancellationToken);
    }

    [HttpPost("conversations/{conversationId:int}/messages/menu")]
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMenu(int conversationId, CancellationToken cancellationToken)
    {

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppMessageDto>("send-menu", conversationId, JsonSerializer.SerializeToElement(new { }), Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendMenu(conversationId, cancellationToken);
    }

    [HttpPost("conversations/{conversationId:int}/messages/quick-reply")]
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendQuickReply(
        int conversationId,
        [FromBody] SendWhatsAppQuickReplyDto dto,
        CancellationToken cancellationToken)
    {

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppMessageDto>("send-quick-reply", conversationId, JsonSerializer.SerializeToElement(dto), Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendQuickReply(conversationId, dto, cancellationToken);
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

        if (_manualQueue is not null)
            return await _manualQueue.EnqueueMediaAsync(conversationId, file, caption, Request, cancellationToken);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendMediaMessage(conversationId, file, caption, cancellationToken);
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

    private Task<bool> CanAccessVerifiedBranchAsync(int branchId, CancellationToken cancellationToken) => _manualOperations.CanAccessVerifiedBranchAsync(branchId, cancellationToken);

    private Task<bool> CanAccessConversationAsync(WhatsAppConversation conversation, CancellationToken cancellationToken) => _manualOperations.CanAccessConversationAsync(conversation, cancellationToken);

    private Task<ConversationChannelCredentials?> ResolveConversationChannelAsync(
        WhatsAppConversation conversation,
        CancellationToken cancellationToken) => _manualOperations.ResolveConversationChannelAsync(conversation, cancellationToken);

    private Task<TemplateCredentialsResolution> ResolveTemplateCredentialsAsync(
        int? branchId,
        bool requireBusinessAccount,
        CancellationToken cancellationToken) => _manualOperations.ResolveTemplateCredentialsAsync(branchId, requireBusinessAccount, cancellationToken);

    private Task<TemplateRecipientsResolution> ResolveTemplateRecipientsAsync(
        SendWhatsAppTemplateDto dto,
        int? branchId,
        bool requiresPhone,
        CancellationToken cancellationToken) => _manualOperations.ResolveTemplateRecipientsAsync(dto, branchId, requiresPhone, cancellationToken);

    private Task PersistOutboundTemplateMessageAsync(
        int branchId,
        string recipient,
        Customer? customer,
        WhatsAppTemplate template,
        IReadOnlyList<string> parameters,
        string? whatsAppMessageId,
        CancellationToken cancellationToken) => _manualOperations.PersistOutboundTemplateMessageAsync(branchId, recipient, customer, template, parameters, whatsAppMessageId, cancellationToken);

    private static string BuildTemplateMessagePreview(WhatsAppTemplate template, IReadOnlyList<string> parameters) => WhatsAppManualOperations.BuildTemplateMessagePreview(template, parameters);

    private static int CountBodyTemplateParameters(string componentsJson) => WhatsAppManualOperations.CountBodyTemplateParameters(componentsJson);

    private static string? ExtractBodyText(string componentsJson) => WhatsAppManualOperations.ExtractBodyText(componentsJson);

    private static bool IsInternationalWhatsAppPhone(string value) => WhatsAppManualOperations.IsInternationalWhatsAppPhone(value);

    private static string FormatRecipientForError(string recipient) => WhatsAppManualOperations.FormatRecipientForError(recipient);

    private bool CanAccessBranch(int branchId) => _manualOperations.CanAccessBranch(branchId);

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

    private static string RenderQuickReplyTemplate(string template, WhatsAppConversation conversation) => WhatsAppManualOperations.RenderQuickReplyTemplate(template, conversation);

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

    private static string NormalizeWhatsAppPhone(string? value) => WhatsAppManualOperations.NormalizeWhatsAppPhone(value);

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

    private Task<WhatsAppAttentionDto> ToAttentionDtoAsync(WhatsAppConversation conversation, CancellationToken ct) => _manualOperations.ToAttentionDtoAsync(conversation, ct);

    private static string AttentionModeToApi(WhatsAppAttentionMode mode) => WhatsAppManualOperations.AttentionModeToApi(mode);

    private static string ConversationStatusToApi(WhatsAppConversationStatus status) => WhatsAppManualOperations.ConversationStatusToApi(status);

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
