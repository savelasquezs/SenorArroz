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

using SenorArroz.API.Controllers;

namespace SenorArroz.API.Services;

[NonController]
public sealed class WhatsAppManualOperations : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;
    private readonly IWhatsAppCloudClient _whatsAppCloudClient;
    private readonly IWhatsAppNotificationService _whatsAppNotificationService;
    private readonly WhatsAppCloudOptions _whatsAppOptions;
    private readonly ILogger<WhatsAppController> _logger;
    private readonly WhatsAppAttentionService _attentionService;
    private readonly WhatsAppCommerceFlowService _commerceFlow;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IInventoryService? _inventory;
    private readonly bool _externalProcessing;
    private int TenantId => _currentTenant?.TenantId ?? _commerceFlow.TenantId;

    private readonly WhatsAppWebhookProcessor _webhookProcessor;

    public WhatsAppManualOperations(IApplicationDbContext db, ICurrentUser currentUser, IBranchContext branchContext,
        IClock clock, IWhatsAppCloudClient cloud, IWhatsAppNotificationService notifications,
        IOptions<WhatsAppCloudOptions> whatsAppOptions, ILogger<WhatsAppController> logger,
        WhatsAppAttentionService attention, WhatsAppCommerceFlowService commerceFlow,
        WhatsAppWebhookProcessor processor, ICurrentTenant? currentTenant = null, IInventoryService? inventory = null,
        IOptions<WhatsAppProcessingOptions>? processing = null)
    {
        _db = db;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _clock = clock;
        _whatsAppCloudClient = cloud;
        _whatsAppNotificationService = notifications;
        _whatsAppOptions = whatsAppOptions.Value;
        _logger = logger;
        _attentionService = attention;
        _commerceFlow = commerceFlow;
        _webhookProcessor = processor;
        _currentTenant = currentTenant;
        _inventory = inventory;
        _externalProcessing = processing?.Value.External == true;
    }

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
            _logger.LogWarning("WhatsApp template sync failed. Category=meta_sync_failed");
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

            RequireKnownOutcome(result);
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
                _logger.LogWarning("WhatsApp template send failed. TemplateId={TemplateId}", template.Id);
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
        RequireKnownOutcome(result);

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
            _logger.LogWarning("WhatsApp outbound message failed. ConversationId={ConversationId}", conversationId);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar el mensaje."));
        }

        _logger.LogInformation("WhatsApp outbound message sent for conversation {ConversationId}", conversationId);
        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Mensaje enviado."));
    }

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
        RequireKnownOutcome(result);
        var now = _clock.UtcNow; var message = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = result.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = string.IsNullOrWhiteSpace(product.CommercialProfile?.PhotoUrl) ? WhatsAppMessageType.Text : WhatsAppMessageType.Image, TextBody = text, MediaUrl = product.CommercialProfile?.PhotoUrl, Status = result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed, Timestamp = now, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, RawPayload = JsonSerializer.Serialize(new { action = "send_product_details", productId, result.Success, result.ErrorMessage }) };
        _db.WhatsAppMessages.Add(message); conversation.LastMessageAt = now; conversation.LastMessagePreview = text; await _db.SaveChangesAsync(cancellationToken);
        if (!result.Success) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar el producto.")); await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Detalle de producto enviado."));
    }

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
            RequireKnownOutcome(result);
            if (!result.Success) { lastError = result.ErrorMessage; break; }
            var timestamp = _clock.UtcNow;
            var message = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = result.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = WhatsAppMessageType.Image, TextBody = caption, MediaUrl = menuImages[index], Status = WhatsAppMessageStatus.Sent, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, Timestamp = timestamp, RawPayload = JsonSerializer.Serialize(new { action = "send_menu", mode = "image", slot = index + 1, result.Success }) };
            _db.WhatsAppMessages.Add(message); sentMessages.Add(message);
            conversation.LastMessageAt = timestamp; conversation.LastMessagePreview = "Carta enviada";
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        }
        var usedFallback = sentMessages.Count != menuImages.Count;
        if (usedFallback)
        {
            var fallback = await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, text, cancellationToken);
            RequireKnownOutcome(fallback);
            if (fallback.Success)
            {
                var fallbackMessage = new WhatsAppMessage { ConversationId = conversation.Id, WhatsAppMessageId = fallback.WhatsAppMessageId, Direction = WhatsAppMessageDirection.Outbound, Type = WhatsAppMessageType.Text, TextBody = text, Status = WhatsAppMessageStatus.Sent, SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null, Timestamp = _clock.UtcNow, RawPayload = JsonSerializer.Serialize(new { action = "send_menu", mode = "url_fallback", imageError = lastError }) };
                _db.WhatsAppMessages.Add(fallbackMessage); sentMessages.Add(fallbackMessage);
            }
            else if (sentMessages.Count == 0) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(fallback.ErrorMessage ?? lastError ?? "No se pudo enviar la carta."));
        }
        var lastMessage = sentMessages[^1]; conversation.LastMessageAt = lastMessage.Timestamp; conversation.LastMessagePreview = "Carta enviada"; await _db.SaveChangesAsync(cancellationToken);
        if (usedFallback && lastMessage.Type == WhatsAppMessageType.Text)
            await NotifyWhatsAppMessageCreatedAsync(lastMessage.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(lastMessage), usedFallback ? "Carta enviada; se usó el enlace como respaldo." : "Carta enviada como imagen."));
    }

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
        RequireKnownOutcome(result);

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
            _logger.LogWarning("WhatsApp quick reply failed. ConversationId={ConversationId}", conversationId);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar la respuesta rápida."));
        }

        _logger.LogInformation("WhatsApp quick reply sent for conversation {ConversationId}", conversationId);
        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Respuesta rápida enviada."));
    }

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMediaMessage(
        int conversationId,
        IFormFile? file,
        [FromForm] string? caption,
        CancellationToken cancellationToken, string? stagedUrl = null)
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

        string? storageUrl = stagedUrl;
        if (storageUrl is null) try
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
            _logger.LogError("WhatsApp outbound storage failed. ConversationId={ConversationId} Category={Category}", conversationId, ex.GetType().Name);
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
            RequireKnownOutcome(sendResult);
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
            _logger.LogWarning("WhatsApp outbound media failed. ConversationId={ConversationId}", conversationId);
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(sendResult.ErrorMessage ?? uploadResult.ErrorMessage ?? "No se pudo enviar el archivo."));
        }

        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Archivo enviado."));
    }


    public async Task<bool> CanAccessVerifiedBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        _branchContext.EnsureAccess(branchId);

        return await _db.WhatsAppBranchSettings
            .AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId && x.IsActive && x.IsVerified, cancellationToken);
    }

    public async Task<bool> CanAccessConversationAsync(WhatsAppConversation conversation, CancellationToken cancellationToken)
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

    public async Task<ConversationChannelCredentials?> ResolveConversationChannelAsync(
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

    public sealed record ConversationChannelCredentials(string PhoneNumberId, string AccessToken);

    public async Task<TemplateCredentialsResolution> ResolveTemplateCredentialsAsync(
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

    public async Task<TemplateRecipientsResolution> ResolveTemplateRecipientsAsync(
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

    public async Task PersistOutboundTemplateMessageAsync(
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

    public static string BuildTemplateMessagePreview(WhatsAppTemplate template, IReadOnlyList<string> parameters)
    {
        var body = ExtractBodyText(template.Components);
        if (string.IsNullOrWhiteSpace(body))
            return $"Plantilla: {template.Name}";

        for (var i = 0; i < parameters.Count; i++)
            body = body.Replace($"{{{{{i + 1}}}}}", parameters[i] ?? string.Empty, StringComparison.Ordinal);

        return body;
    }

    public static int CountBodyTemplateParameters(string componentsJson)
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

    public static string? ExtractBodyText(string componentsJson)
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

    public static bool IsInternationalWhatsAppPhone(string value)
    {
        return Regex.IsMatch(value, @"^[1-9]\d{7,14}$");
    }

    public static string FormatRecipientForError(string recipient) =>
        IsInternationalWhatsAppPhone(recipient) ? $"+{recipient}" : "usuario de WhatsApp";

    public bool CanAccessBranch(int branchId)
    {
        _branchContext.EnsureAccess(branchId);
        return true;
    }

    public static string RenderQuickReplyTemplate(string template, WhatsAppConversation conversation)
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

    public static string NormalizeWhatsAppPhone(string? value) => OnlyDigits(value);

    public async Task<WhatsAppAttentionDto> ToAttentionDtoAsync(WhatsAppConversation conversation, CancellationToken ct)
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

    public static string AttentionModeToApi(WhatsAppAttentionMode mode) => mode switch { WhatsAppAttentionMode.Ai => "ai", WhatsAppAttentionMode.Human => "human", WhatsAppAttentionMode.WaitingForHuman => "waitingForHuman", WhatsAppAttentionMode.Paused => "paused", WhatsAppAttentionMode.Closed => "closed", _ => "human" };

    public static string ConversationStatusToApi(WhatsAppConversationStatus status) => status switch
    {
        WhatsAppConversationStatus.Open => "open",
        WhatsAppConversationStatus.Pending => "pending",
        WhatsAppConversationStatus.Closed => "closed",
        WhatsAppConversationStatus.Archived => "archived",
        _ => "open"
    };

    public sealed record TemplateCredentialsResolution(
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

    public sealed record TemplateRecipient(string Recipient, Customer? Customer);

    public sealed record TemplateRecipientsResolution(
        bool Forbidden,
        string? ErrorMessage,
        IReadOnlyList<TemplateRecipient> Recipients)
    {
        public static TemplateRecipientsResolution Denied() => new(true, null, []);
        public static TemplateRecipientsResolution Failed(string errorMessage) => new(false, errorMessage, []);
    }
    private void RequireKnownOutcome(WhatsAppCloudSendResult result)
    {
        if (_externalProcessing && result.OutcomeUncertain)
            throw new IOException("WhatsApp send outcome requires reconciliation.");
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
