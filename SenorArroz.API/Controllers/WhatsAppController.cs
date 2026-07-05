using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IWhatsAppCloudClient _whatsAppCloudClient;
    private readonly IWhatsAppNotificationService _whatsAppNotificationService;
    private readonly IFirebaseGcsStorage _firebaseStorage;
    private readonly FirebaseStorageOptions _firebaseOptions;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IWhatsAppCloudClient whatsAppCloudClient,
        IWhatsAppNotificationService whatsAppNotificationService,
        IFirebaseGcsStorage firebaseStorage,
        IOptions<FirebaseStorageOptions> firebaseOptions,
        ILogger<WhatsAppController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _whatsAppCloudClient = whatsAppCloudClient;
        _whatsAppNotificationService = whatsAppNotificationService;
        _firebaseStorage = firebaseStorage;
        _firebaseOptions = firebaseOptions.Value;
        _logger = logger;
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
        if (search.BranchId.HasValue && !CanAccessBranch(search.BranchId.Value))
            return Forbid();

        var query = _db.WhatsAppQuickReplies
            .AsNoTracking()
            .Include(x => x.Branch)
            .AsQueryable();

        if (!Roles.IsSuperadmin(_currentUser.Role))
            query = query.Where(x => x.BranchId == _currentUser.BranchId);
        else if (search.BranchId.HasValue)
            query = query.Where(x => x.BranchId == search.BranchId.Value);

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

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var processedAny = await ProcessWebhookPayloadAsync(document.RootElement, webhookEvent, createdMessages, cancellationToken);
            webhookEvent.Processed = processedAny;
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var message in createdMessages.Where(x => x.Id > 0))
            {
                await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
            }
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
        var conversations = conversationEntities.Select(ToConversationDto).ToList();

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

        if (!Roles.IsSuperadmin(_currentUser.Role))
            query = query.Where(x => x.BranchId == _currentUser.BranchId);

        return query.Select(x => x.BranchId).Distinct();
    }

    private async Task<bool> CanAccessVerifiedBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        if (!Roles.IsSuperadmin(_currentUser.Role) && _currentUser.BranchId != branchId)
            return false;

        return await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId && x.IsActive && x.IsVerified, cancellationToken);
    }

    private bool CanAccessBranch(int branchId)
    {
        return Roles.IsSuperadmin(_currentUser.Role) || _currentUser.BranchId == branchId;
    }

    private async Task<(ActionResult<ApiResponse<WhatsAppQuickReplyDto>>? Error, int BranchId, string Shortcut)> ValidateQuickReplyPayloadAsync(
        UpsertWhatsAppQuickReplyDto dto,
        int? currentId,
        CancellationToken cancellationToken)
    {
        var branchId = Roles.IsSuperadmin(_currentUser.Role)
            ? dto.BranchId.GetValueOrDefault()
            : _currentUser.BranchId;

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

                processedAny |= await ProcessInboundMessagesAsync(value, setting, webhookEvent, createdMessages, cancellationToken);
                processedAny |= await ProcessStatusesAsync(value, webhookEvent, cancellationToken);
            }
        }

        return processedAny;
    }

    private async Task<bool> ProcessInboundMessagesAsync(
        JsonElement value,
        WhatsAppBranchSetting setting,
        WhatsAppWebhookEvent webhookEvent,
        List<WhatsAppMessage> createdMessages,
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
                conversation = new WhatsAppConversation
                {
                    BranchId = setting.BranchId,
                    PhoneNumber = from,
                    Status = WhatsAppConversationStatus.Open
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
                RawPayload = messageElement.GetRawText()
            };
            _db.WhatsAppMessages.Add(message);
            createdMessages.Add(message);

            webhookEvent.EventType = "message";
            webhookEvent.WhatsAppMessageId ??= messageId;
            processed = true;
        }

        return processed;
    }

    private async Task<bool> ProcessStatusesAsync(JsonElement value, WhatsAppWebhookEvent webhookEvent, CancellationToken cancellationToken)
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
                .FirstOrDefaultAsync(x => x.WhatsAppMessageId == messageId, cancellationToken);
            if (message is null)
                continue;

            message.Status = status;
            webhookEvent.EventType = "status";
            webhookEvent.WhatsAppMessageId ??= messageId;
            processed = true;
        }

        return processed;
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
        return message.TryGetProperty("text", out var text)
            ? TryGetString(text, "body")
            : null;
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

    private static WhatsAppConversationDto ToConversationDto(WhatsAppConversation conversation) => new()
    {
        Id = conversation.Id,
        BranchId = conversation.BranchId,
        BranchName = conversation.Branch?.Name,
        CustomerId = conversation.CustomerId,
        CustomerName = conversation.Customer?.Name,
        PhoneNumber = conversation.PhoneNumber,
        ContactName = conversation.ContactName,
        Status = ConversationStatusToApi(conversation.Status),
        LastMessageAt = conversation.LastMessageAt,
        LastMessagePreview = conversation.LastMessagePreview,
        UnreadCount = conversation.UnreadCount,
        CreatedAt = conversation.CreatedAt,
        UpdatedAt = conversation.UpdatedAt
    };

    private static WhatsAppMessageDto ToMessageDto(WhatsAppMessage message) => new()
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
        Timestamp = message.Timestamp,
        CreatedAt = message.CreatedAt
    };

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
}
