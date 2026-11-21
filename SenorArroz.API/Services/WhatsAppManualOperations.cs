using System.Text.Json;
using System.Globalization;
using static SenorArroz.API.Services.WhatsAppWebhookProcessor;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

using SenorArroz.API.Controllers;

namespace SenorArroz.API.Services;

[NonController]
public sealed partial class WhatsAppManualOperations : ControllerBase
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
    private readonly IFirebaseGcsStorage? _menuStorage;
    private readonly bool _externalProcessing;
    private int TenantId => _currentTenant?.TenantId ?? _commerceFlow.TenantId;

    private readonly WhatsAppWebhookProcessor _webhookProcessor;

    public WhatsAppManualOperations(IApplicationDbContext db, ICurrentUser currentUser, IBranchContext branchContext,
        IClock clock, IWhatsAppCloudClient cloud, IWhatsAppNotificationService notifications,
        IOptions<WhatsAppCloudOptions> whatsAppOptions, ILogger<WhatsAppController> logger,
        WhatsAppAttentionService attention, WhatsAppCommerceFlowService commerceFlow,
        WhatsAppWebhookProcessor processor, ICurrentTenant? currentTenant = null, IInventoryService? inventory = null,
        IOptions<WhatsAppProcessingOptions>? processing = null, IFirebaseGcsStorage? menuStorage = null)
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
        _menuStorage = menuStorage;
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
        if (IsStarterTemplate(templateName, normalizedLanguage))
            return BadRequest(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse("Envía esta plantilla desde Nueva conversación."));

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

        if (credentials.BranchId.HasValue)
        {
            var targets = recipients.Recipients.Select(x => x.Recipient).ToList();
            var existingConversations = await _db.WhatsAppConversations.AsNoTracking()
                .Where(x => x.ChannelSettingId == null && x.BranchLineId == null && x.BranchId == credentials.BranchId
                    && (targets.Contains(x.PhoneNumber!) || targets.Contains(x.WhatsAppUserId!))).ToListAsync(cancellationToken);
            if (existingConversations.Any(x => !WhatsAppAttentionService.CanWrite(x, _currentUser.Id)))
                return Conflict(ApiResponse<WhatsAppTemplateSendResultDto>.ErrorResponse("Toma las conversaciones antes de enviarles plantillas."));
        }
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


    public Task<ActionResult<ApiResponse<WhatsAppMessageDto[]>>> SendCommercialAddressChoicesAsync(
        int conversationId, WhatsAppDraftWriteDto request, CancellationToken ct) =>
        SendCommercialChoicesAsync(conversationId, request, null, ct);

    public Task<ActionResult<ApiResponse<WhatsAppMessageDto[]>>> SendCommercialProductChoicesAsync(
        int conversationId, int groupId, WhatsAppDraftWriteDto request, CancellationToken ct) =>
        SendCommercialChoicesAsync(conversationId, request, groupId, ct);

    private async Task<ActionResult<ApiResponse<WhatsAppMessageDto[]>>> SendCommercialChoicesAsync(
        int conversationId, WhatsAppDraftWriteDto request, int? productGroupId, CancellationToken ct)
    {
        ActionResult<ApiResponse<WhatsAppMessageDto[]>> Invalid(string message) =>
            BadRequest(ApiResponse<WhatsAppMessageDto[]>.ErrorResponse(message));

        var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || !await CanAccessConversationAsync(conversation, ct))
            return Forbid();
        var session = await _db.WhatsAppCommerceSessions.AsNoTracking()
            .Where(x => x.ConversationId == conversationId).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (session?.Status != "active" || session.Id != request.SessionId || session.Version != request.ExpectedVersion
            || conversation.AttentionRevision != request.AttentionVersion)
            return Conflict(ApiResponse<WhatsAppMessageDto[]>.ErrorResponse("El pedido o su atención cambió. Revisa el borrador."));

        var order = WhatsAppCommercialDraft.Read(session, conversation);
        if (order.BranchId <= 0 || !await _db.Branches.AnyAsync(x => x.Id == order.BranchId && x.IsActive, ct))
            return Invalid("Elige primero la sucursal que prepara el pedido.");
        if (!await IsServiceWindowOpenAsync(conversation, ct))
            return Invalid("La ventana de atención terminó. Envía una plantilla aprobada.");
        var setting = await ResolveConversationChannelAsync(conversation, ct);
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (setting is null || recipient is null) return Invalid("La línea de esta conversación no está lista para enviar.");

        if (productGroupId.HasValue)
        {
            var source = request.ProductChoiceSource;
            WhatsAppProductSuggestions.ChoiceOffer? offer = null;
            var quantity = 1;
            if (source is not null)
            {
                var text = await _db.WhatsAppMessages.AsNoTracking()
                    .Where(x => x.Id == source.MessageId && x.ConversationId == conversationId
                        && x.Direction == WhatsAppMessageDirection.Inbound && x.Type == WhatsAppMessageType.Text
                        && !x.IsHistorical && x.HiddenAt == null
                        && x.AiProcessingError != WhatsAppCommercialDraft.ProductApplied && x.AiProcessingError != "order_product_unavailable")
                    .Select(x => x.TextBody).SingleOrDefaultAsync(ct);
                if (text is null || source.ChoiceIndex < 0) return Invalid("La sugerencia ya no está disponible.");
                var catalog = await WhatsAppProductSuggestions.Catalog(_db.Products.Where(x =>
                    StorefrontCommerceService.PublicRoles.Contains(x.Category.StorefrontRole))).ToListAsync(ct);
                var match = WhatsAppProductSuggestions.Find(text, catalog).ElementAtOrDefault(source.ChoiceIndex);
                offer = match?.Offer;
                if (offer is null || offer.Products[0].GroupId != productGroupId || offer.Products[0].Id != source.ProductId)
                    return Invalid("Las presentaciones cambiaron. Vuelve a consultar el mensaje.");
                var answerKey = WhatsAppCommercialDraft.SuggestedProductAnswerKey(session.Id, source);
                if (await _db.WhatsAppMessages.AnyAsync(x => x.AgentDispatchKey == answerKey, ct))
                    return Invalid("El cliente ya eligió una opción para esta sugerencia.");
                quantity = match!.Quantity;
            }
            var catalogChoices = WhatsAppCommercialDraft.ChoiceCatalog(_db).Include(x => x.CommercialProfile).Where(x => x.Active);
            var offeredIds = offer?.Products.Select(p => p.Id).ToArray();
            var products = await (offeredIds is null ? catalogChoices.Where(x => x.CommercialProfileId == productGroupId)
                : catalogChoices.Where(x => offeredIds.Contains(x.Id))).OrderBy(x => x.Price)
                .ThenBy(x => x.StorefrontSortOrder).ThenBy(x => x.Id).ToListAsync(ct);
            if (_inventory is not null && products.Count > 0)
            {
                var availability = await _inventory.GetAvailabilityAsync(products.Select(x => x.Id).ToArray(), order.BranchId, ct, true);
                var ordered = order.OrderDetails.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Quantity));
                var available = availability.Where(x => x.Available && (!x.MaximumQuantity.HasValue
                    || x.MaximumQuantity.Value >= ordered.GetValueOrDefault(x.ProductId) + quantity)).Select(x => x.ProductId).ToHashSet();
                products.RemoveAll(x => !available.Contains(x.Id));
            }
            if (products.Count == 0) return Invalid("No hay presentaciones disponibles para la sucursal que prepara.");
            var name = offer?.Name ?? products[0].CommercialProfile!.Name;
            var body = quantity == 1 ? $"Elige una presentación de {name}." : $"Elige la presentación para {quantity} unidades de {name}.";
            var productOptions = products.Select(x => ProductChoice(x, x.CommercialProfile!.Name)).ToArray();
            if (offer?.ChicharronVariants == true)
            {
                var byId = offer.Products.ToDictionary(p => p.Id);
                var presentations = products.Select(x => (Product: x, Presentation: WhatsAppProductSuggestions.ChicharronPresentation(byId[x.Id])))
                    .ToArray();
                var showSize = presentations.Select(p => p.Presentation?.Size).Distinct().Count() > 1;
                productOptions = presentations.Select(x =>
                {
                    if (x.Presentation is not { } presentation) return ProductChoice(x.Product, x.Product.CommercialProfile!.Name);
                    var variant = presentation.WithChicharron ? "Con chicharrón" : "Sin chicharrón";
                    var label = showSize ? $"{WhatsAppProductSuggestions.SizeLabel(presentation.Size)} · {variant.ToLowerInvariant()}" : variant;
                    var price = "$" + x.Product.Price.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));
                    return new CommercialChoice(x.Product.Id, label, label, price, $"{label} · {price}", x.Product.Price);
                }).ToArray();
            }
            if (source is not null && productOptions.Length <= 3
                && productOptions.Any(x => x.ButtonLabel != x.Preview) && productOptions.All(x => x.ButtonLabel.Length <= 20))
                body += "\n" + string.Join("\n", productOptions.Select(option => option.Preview));
            return await SendChoicePromptAsync(conversation, session, order, setting, recipient,
                body, "Ver opciones", productOptions, productGroupId, ct, source, quantity);
        }

        if (order.Type is not (OrderType.Delivery or OrderType.Reservation)) return Invalid("El pedido no requiere una dirección.");
        if (order.CustomerId is not int customerId || conversation.CustomerId != customerId)
            return Invalid("Vincula el cliente del pedido con esta conversación antes de compartir sus direcciones.");
        var addresses = await _db.AddressBranches.AsNoTracking()
            .Where(x => x.BranchId == order.BranchId && x.Branch.IsActive && x.IsCovered && x.DeliveryFee >= 0
                && x.Address.CustomerId == customerId && x.Address.Customer.Active && x.Address.AddressText.Trim() != "")
            .OrderByDescending(x => x.Address.IsPrimary).ThenBy(x => x.AddressId)
            .Select(x => new { x.AddressId, x.Address.AddressText, x.Address.AdditionalInfo }).ToListAsync(ct);
        if (addresses.Count == 0) return Invalid("Este cliente no tiene direcciones con cobertura confirmada en la sucursal que prepara.");
        var options = addresses.Select(x =>
        {
            var label = string.Join(", ", new[] { x.AddressText.Trim(), x.AdditionalInfo?.Trim() }.Where(y => !string.IsNullOrWhiteSpace(y)));
            return new CommercialChoice(x.AddressId, label, "", label, label);
        }).ToArray();
        return await SendChoicePromptAsync(conversation, session, order, setting, recipient,
            addresses.Count == 1 ? "¿Entregamos en esta dirección?" : "Elige dónde quieres recibir tu pedido.",
            "Ver direcciones", options, null, ct);
    }

    private async Task<ActionResult<ApiResponse<WhatsAppMessageDto[]>>> SendChoicePromptAsync(
        WhatsAppConversation conversation, WhatsAppCommerceSession session,
        CreateOrderDto order, ConversationChannelCredentials setting,
        string recipient, string body, string listButton, CommercialChoice[] options, int? productGroupId, CancellationToken ct,
        WhatsAppProductChoiceSourceDto? source = null, int quantity = 1)
    {
        var prefix = productGroupId.HasValue ? WhatsAppCommercialDraft.ProductChoicePrefix : WhatsAppCommercialDraft.AddressChoicePrefix;
        var scope = WhatsAppCommercialDraft.ChoiceScope(prefix, productGroupId, source);
        var requestId = Guid.NewGuid();
        var sent = new List<WhatsAppMessageDto>();
        var index = 0;
        foreach (var group in options.Chunk(10))
        {
            string RowId(CommercialChoice option) => $"{prefix}{requestId:N}:{option.Id}";
            var canUseButtons = group.Length <= 3 && group.All(x => x.ButtonLabel.Length <= 20
                && (x.ButtonLabel == x.Preview || source is not null && body.Contains(x.Preview, StringComparison.Ordinal)))
                && group.Select(x => x.ButtonLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count() == group.Length;
            var result = canUseButtons
                ? await _whatsAppCloudClient.SendReplyButtonsMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, body,
                    group.Select(x => new WhatsAppReplyButton(RowId(x), x.ButtonLabel)).ToArray(), ct)
                : await _whatsAppCloudClient.SendListMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, body, listButton,
                    group.Select((x, i) => new WhatsAppListOption(RowId(x),
                        LimitChoiceText(x.ListTitle.Length > 0 ? x.ListTitle : $"Dirección {index + i + 1}", 24),
                        LimitChoiceText(x.ListDescription, 72))).ToArray(), ct);
            RequireKnownOutcome(result);
            if (result.OutcomeUncertain)
                return BadRequest(ApiResponse<WhatsAppMessageDto[]>.ErrorResponse("Meta no confirmó el resultado. Revisa la conversación antes de volver a enviar las opciones."));
            if (!result.Success)
                return BadRequest(ApiResponse<WhatsAppMessageDto[]>.ErrorResponse(sent.Count > 0
                    ? "Se enviaron algunas opciones. Meta rechazó las restantes."
                    : result.ErrorMessage ?? "No se pudieron enviar las opciones."));

            var prompt = new WhatsAppCommercialDraft.ChoicePrompt(requestId, session.Id, conversation.AttentionRevision,
                order.CustomerId, order.BranchId, order.Type, order.AddressId,
                AddressIds: productGroupId.HasValue ? null : group.Select(x => x.Id).ToArray(),
                ProductGroupId: productGroupId, ProductPrices: productGroupId.HasValue ? group.ToDictionary(x => x.Id, x => x.Price) : null,
                ProductChoiceSource: source, Quantity: quantity);
            var message = new WhatsAppMessage
            {
                ConversationId = conversation.Id,
                WhatsAppMessageId = result.WhatsAppMessageId,
                Direction = WhatsAppMessageDirection.Outbound,
                Type = WhatsAppMessageType.Text,
                TextBody = body + string.Concat(group.Where(x => source is null || !body.Contains(x.Preview, StringComparison.Ordinal))
                    .Select((x, i) => $"\n{index + i + 1}. {x.Preview}")),
                Status = WhatsAppMessageStatus.Sent,
                SentByUserId = _currentUser.Id,
                Timestamp = _clock.UtcNow,
                AgentDispatchKey = $"{scope}{requestId:N}:{index}",
                RawPayload = JsonSerializer.Serialize(prompt, WhatsAppCommercialDraft.JsonOptions)
            };
            _db.WhatsAppMessages.Add(message);
            await _db.SaveChangesAsync(ct);
            await NotifyWhatsAppMessageCreatedAsync(message.Id, ct);
            sent.Add(ToMessageDto(message));
            index += group.Length;
        }
        return Ok(ApiResponse<WhatsAppMessageDto[]>.SuccessResponse(sent.ToArray(), "Opciones enviadas."));
    }

    private sealed record CommercialChoice(int Id, string ButtonLabel, string ListTitle, string ListDescription, string Preview, int Price = 0);

    private static CommercialChoice ProductChoice(Product product, string groupName)
    {
        var size = product.StorefrontVariantLabel?.Trim();
        if (string.IsNullOrEmpty(size))
            size = product.Name.StartsWith(groupName + " ", StringComparison.OrdinalIgnoreCase)
                ? product.Name[groupName.Length..].Trim() : product.Name;
        if (size.Equals("Súper", StringComparison.OrdinalIgnoreCase)) size = "Súper familiar";
        var price = "$" + product.Price.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));
        var details = product.ServesPeopleMin is int min && product.ServesPeopleMax is int max
            ? $"{price} · {(min == max ? min.ToString() : $"{min}–{max}")} personas" : price;
        return new(product.Id, $"{size} · {price}", size, details, $"{size} · {details}", product.Price);
    }

    private static string LimitChoiceText(string label, int max)
    {
        if (label.Length <= max) return label;
        var cut = char.IsHighSurrogate(label[max - 2]) ? max - 2 : max - 1;
        return label[..cut] + "…";
    }

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMessage(
        int conversationId,
        [FromBody] SendWhatsAppMessageDto dto,
        CancellationToken cancellationToken, bool forwarded = false, (string Type, JsonElement Content)? shared = null)
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
        var ownsAttention = WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id);
        if ((!forwarded && !ownsAttention) || !await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();
        if (conversation.AttentionMode == WhatsAppAttentionMode.Closed || forwarded && IsClosedForwardDestination(conversation))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación está cerrada. Debes reabrirla antes de enviar mensajes."));

        if (!await IsServiceWindowOpenAsync(conversation, cancellationToken))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó. Envía una plantilla aprobada."));
        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene teléfono ni identidad de WhatsApp para responder."));

        var quoted = await ResolveQuotedMessageAsync(conversationId, dto.ReplyToMessageId, cancellationToken);
        if (dto.ReplyToMessageId.HasValue && string.IsNullOrWhiteSpace(quoted))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Ya no puedes citar ese mensaje."));
        var timestamp = _clock.UtcNow;
        var result = shared is { } structure
            ? await _whatsAppCloudClient.SendSharedContentAsync(setting.PhoneNumberId, setting.AccessToken, recipient,
                structure.Type, structure.Content, cancellationToken)
            : quoted is null
            ? await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, text, cancellationToken)
            : await _whatsAppCloudClient.SendTextReplyAsync(setting.PhoneNumberId, setting.AccessToken, recipient, text, quoted, cancellationToken);
        RequireKnownOutcome(result);
        if (!result.Success && ClientRequestId is not null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar el mensaje."));

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
            RawPayload = shared is { } content
                ? JsonSerializer.Serialize(new Dictionary<string, object> { ["type"] = content.Type, [content.Type] = content.Content, ["_pos_forwarded"] = true })
                : JsonSerializer.Serialize(new
            {
                to = recipient,
                text,
                _pos_forwarded = forwarded,
                _pos_client_request_id = ClientRequestId,
                context = quoted is null ? null : new { message_id = quoted },
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
        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken, clearPendingResponse: !forwarded || ownsAttention);
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(message), "Mensaje enviado."));
    }

    private Task<string?> ResolveQuotedMessageAsync(int conversationId, int? replyId, CancellationToken ct) =>
        replyId.HasValue ? _db.WhatsAppMessages.AsNoTracking()
            .Where(message => message.Id == replyId && message.ConversationId == conversationId && message.HiddenAt == null)
            .Select(message => message.WhatsAppMessageId).SingleOrDefaultAsync(ct) : Task.FromResult<string?>(null);

    internal async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> ReactToMessageAsync(
        int conversationId, int messageId, WhatsAppMessageReactionDto request, CancellationToken ct)
    {
        if (request.Emoji is not ("" or "👍" or "❤️" or "😂" or "😮" or "😢" or "🙌"))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Elige una reacción disponible."));
        var conversation = await _db.WhatsAppConversations.SingleOrDefaultAsync(chat => chat.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || !await CanAccessConversationAsync(conversation, ct))
            return Forbid();
        if (!await IsServiceWindowOpenAsync(conversation, ct))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó."));
        var target = await _db.WhatsAppMessages.SingleOrDefaultAsync(message => message.Id == messageId
            && message.ConversationId == conversationId && message.HiddenAt == null, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.WhatsAppMessageId)) return NotFound();
        var setting = await ResolveConversationChannelAsync(conversation, ct);
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (setting is null || recipient is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La línea de WhatsApp no está disponible."));

        var emoji = request.Emoji.Length > 0 ? request.Emoji : null;
        if (target.BusinessReaction == emoji)
            return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(target)));
        var result = await _whatsAppCloudClient.SendReactionAsync(setting.PhoneNumberId, setting.AccessToken, recipient,
            target.WhatsAppMessageId, request.Emoji, ct);
        RequireKnownOutcome(result);
        if (!result.Success)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo guardar la reacción en WhatsApp."));

        target.BusinessReaction = emoji;
        target.BusinessReactionAt = DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(_clock.UtcNow).ToUnixTimeSeconds()).UtcDateTime;
        await _db.SaveChangesAsync(ct);
        try { await _webhookProcessor.NotifyWhatsAppMessageCreatedAsync(target.Id, ct, isUpdate: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "WhatsApp reaction saved; realtime notification failed. MessageId={MessageId}", target.Id); }
        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(target)));
    }

    internal async Task<string> SendClosedDayReplyAsync(WhatsAppConversation conversation, string text,
        string dispatchKey, Func<CancellationToken, Task<bool>> stillEligible, CancellationToken ct)
    {
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        var setting = await ResolveConversationChannelAsync(conversation, ct);
        if (setting is null || recipient is null) return "skipped";

        var message = new WhatsAppMessage
        {
            ConversationId = conversation.Id,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = WhatsAppMessageType.Text,
            TextBody = text,
            Status = WhatsAppMessageStatus.Failed,
            SentByUserId = _currentUser.Id,
            Timestamp = _clock.UtcNow,
            AgentDispatchKey = dispatchKey,
            RawPayload = JsonSerializer.Serialize(new { origin = "closed_day_follow_up", outcome = "reserved" })
        };
        _db.WhatsAppMessages.Add(message);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_whatsapp_message_agent_dispatch_key" })
        {
            return "skipped";
        }

        if (!await stillEligible(ct))
        {
            _db.WhatsAppMessages.Remove(message);
            await _db.SaveChangesAsync(ct);
            return "skipped";
        }

        message.Timestamp = _clock.UtcNow;
        var result = await _whatsAppCloudClient.SendTextMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, text, ct);
        var outcome = result.OutcomeUncertain ? "review" : result.Success ? "sent" : "failed";
        message.WhatsAppMessageId = result.WhatsAppMessageId;
        message.Status = outcome == "sent" ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed;
        message.RawPayload = JsonSerializer.Serialize(new { origin = "closed_day_follow_up", outcome });
        await _db.SaveChangesAsync(ct);
        if (outcome != "sent") return outcome;

        var latest = _db.WhatsAppConversations.Where(c => c.Id == conversation.Id
            && (!c.LastMessageAt.HasValue || c.LastMessageAt <= message.Timestamp));
        if (_db.Database.IsRelational())
            await latest.ExecuteUpdateAsync(update => update.SetProperty(c => c.LastMessageAt, message.Timestamp)
                .SetProperty(c => c.LastMessagePreview, text), ct);
        else if (await latest.AnyAsync(ct))
        {
            conversation.LastMessageAt = message.Timestamp;
            conversation.LastMessagePreview = text;
            await _db.SaveChangesAsync(ct);
        }
        await NotifyWhatsAppMessageCreatedAsync(message.Id, ct);
        return outcome;
    }

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendProductDetails(int conversationId, int productId, CancellationToken cancellationToken)
    {
        var conversation = await _db.WhatsAppConversations.FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null) return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || !await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
        var product = await _db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.CommercialProfile).FirstOrDefaultAsync(x => x.Id == productId && x.Category.BranchId == conversation.BranchId, cancellationToken);
        if (product is null) return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Producto no encontrado."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));
        if (!await IsServiceWindowOpenAsync(conversation, cancellationToken))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó. Envía una plantilla aprobada."));
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
        if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || !await CanAccessConversationAsync(conversation, cancellationToken)) return Forbid();
        var branch = await _db.Branches.AsNoTracking().FirstAsync(x => x.Id == conversation.BranchId, cancellationToken);
        var menuImages = new[] { branch.MenuImageUrl1, branch.MenuImageUrl2 }
            .OfType<string>().Where(url => !string.IsNullOrWhiteSpace(url)).ToArray();
        if (menuImages.Length == 0)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Esta sucursal todavía no tiene una carta configurada."));
        if (!await IsServiceWindowOpenAsync(conversation, cancellationToken))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó. Envía una plantilla aprobada."));
        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));
        WhatsAppMessage? lastMessage = null;
        for (var index = 0; index < menuImages.Length; index++)
        {
            var caption = index == 0 ? "Nuestra carta actual" : "Carta (continuación)";
            var result = await _whatsAppCloudClient.SendImageLinkMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient, menuImages[index], caption, cancellationToken);
            RequireKnownOutcome(result);
            if (!result.Success)
            {
                var error = result.ErrorMessage ?? "No se pudo enviar la imagen de la carta.";
                if (index > 0) error = $"Solo se envió {index} de {menuImages.Length} imágenes de la carta. {error}";
                return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(error));
            }

            var timestamp = _clock.UtcNow;
            lastMessage = new WhatsAppMessage
            {
                ConversationId = conversation.Id,
                WhatsAppMessageId = result.WhatsAppMessageId,
                Direction = WhatsAppMessageDirection.Outbound,
                Type = WhatsAppMessageType.Image,
                TextBody = caption,
                MediaUrl = menuImages[index],
                Status = WhatsAppMessageStatus.Sent,
                SentByUserId = _currentUser.Id > 0 ? _currentUser.Id : null,
                Timestamp = timestamp,
                RawPayload = JsonSerializer.Serialize(new { action = "send_menu", mode = "image", slot = index + 1, result.Success })
            };
            _db.WhatsAppMessages.Add(lastMessage);
            conversation.LastMessageAt = timestamp;
            conversation.LastMessagePreview = "Carta enviada";
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyWhatsAppMessageCreatedAsync(lastMessage.Id, cancellationToken);
        }

        return Ok(ApiResponse<WhatsAppMessageDto>.SuccessResponse(ToMessageDto(lastMessage!), "Carta enviada como imagen."));
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
        if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || !await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();

        var quickReply = await _db.WhatsAppQuickReplies
            .FirstOrDefaultAsync(x => x.Id == dto.QuickReplyId && x.BranchId == conversation.BranchId && x.IsActive, cancellationToken);
        if (quickReply is null)
            return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Respuesta rápida no encontrada o inactiva para esta sucursal."));
        if (quickReply.ReplyMode != WhatsAppQuickReply.Send)
            return Conflict(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Prepara y revisa esta respuesta en el campo de mensaje antes de enviarla."));

        var text = RenderQuickReplyTemplate(quickReply.MessageTemplate, conversation).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La respuesta rápida no tiene contenido para enviar."));
        if (text.Length > 4096)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La respuesta rápida renderizada supera 4096 caracteres."));

        if (!await IsServiceWindowOpenAsync(conversation, cancellationToken))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó. Envía una plantilla aprobada."));
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
        if (!result.Success && ClientRequestId is not null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(result.ErrorMessage ?? "No se pudo enviar la respuesta rápida."));

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
                _pos_client_request_id = ClientRequestId,
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

    internal const int MaxStickerBytes = 500 * 1024;
    internal const string UnsupportedSticker = "El sticker debe ser un WebP válido de hasta 100 KB si es estático o 500 KB si es animado.";

    internal static bool IsSupportedSticker(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 21 or > MaxStickerBytes || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes[8..12].SequenceEqual("WEBP"u8)) return false;
        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8X"u8)) return (bytes[20] & 2) != 0 || bytes.Length <= 100 * 1024;
        return bytes.Length <= 100 * 1024 && (chunk.SequenceEqual("VP8 "u8) || chunk.SequenceEqual("VP8L"u8));
    }

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> ForwardMessageAsync(
        int conversationId, WhatsAppForwardMessageDto request, CancellationToken ct)
    {
        if (request.SourceConversationId == conversationId) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Elige otra conversación."));
        var origin = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SourceConversationId, ct);
        if (origin is null) return NotFound();
        if (!await CanAccessConversationAsync(origin, ct)) return Forbid();
        var source = await _db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(x => x.ConversationId == origin.Id
            && x.Id == request.MessageId && x.HiddenAt == null && x.Status != WhatsAppMessageStatus.Failed, ct);
        if (source is null) return NotFound();

        var content = ToMessageDto(source);
        if (content.Type is "location" or "contacts")
        {
            if (content.Location is null && content.Contacts is not { Count: > 0 })
                return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El mensaje no contiene datos para reenviar."));
            using var raw = JsonDocument.Parse(source.RawPayload!);
            return await SendMessage(conversationId, new() { Text = content.Type == "location" ? "Ubicación compartida" : "Contacto compartido" },
                ct, forwarded: true, shared: (content.Type, raw.RootElement.GetProperty(content.Type)));
        }
        if (source.Type == WhatsAppMessageType.Text)
            return await SendMessage(conversationId, new() { Text = source.TextBody }, ct, forwarded: true);

        if (string.IsNullOrWhiteSpace(source.MediaUrl))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El archivo original no está disponible."));
        var destination = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (destination is null) return NotFound();
        if (!await CanAccessConversationAsync(destination, ct)) return Forbid();
        if (IsClosedForwardDestination(destination))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación está cerrada. Debes reabrirla antes de reenviar mensajes."));
        if (!await IsServiceWindowOpenAsync(destination, ct))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó."));

        var sticker = source.Type == WhatsAppMessageType.Sticker;
        var maxBytes = sticker ? MaxStickerBytes : 69_000_000;
        if (source.MediaFileSize > maxBytes)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El archivo original supera el tamaño permitido."));
        var bytes = await _webhookProcessor.DownloadStoredMediaAsync(source.MediaUrl, maxBytes, ct);
        if (bytes is null) return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El archivo original no está disponible."));
        var mimeType = sticker ? "image/webp" : source.MediaMimeType ?? "application/octet-stream";
        var name = source.MediaFileName ?? $"archivo{ExtensionFromContentType(mimeType, source.Type)}";
        using var stream = new MemoryStream(bytes, writable: false);
        var file = new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = mimeType };
        return await SendMediaMessage(conversationId, file, source.TextBody == name ? null : source.TextBody, ct, asSticker: sticker, forwarded: true);
    }

    private static bool IsClosedForwardDestination(WhatsAppConversation destination) =>
        destination.AttentionMode == WhatsAppAttentionMode.Closed
        || destination.Status is WhatsAppConversationStatus.Closed or WhatsAppConversationStatus.Archived;

    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto>>> SendMediaMessage(
        int conversationId,
        IFormFile? file,
        [FromForm] string? caption,
        CancellationToken cancellationToken, string? stagedUrl = null, int? replyToMessageId = null, bool asSticker = false, bool forwarded = false)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Seleccione un archivo."));
        if (asSticker && (file.Length > MaxStickerBytes || !string.Equals(file.ContentType, "image/webp", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(UnsupportedSticker));

        var conversation = await _db.WhatsAppConversations
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        if (conversation is null)
            return NotFound(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Conversación no encontrada."));
        var ownsAttention = WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id);
        if ((!forwarded && !ownsAttention) || !await CanAccessConversationAsync(conversation, cancellationToken))
            return Forbid();
        if (forwarded && IsClosedForwardDestination(conversation))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación está cerrada. Debes reabrirla antes de reenviar mensajes."));

        if (!await IsServiceWindowOpenAsync(conversation, cancellationToken))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La ventana de atención terminó. Envía una plantilla aprobada."));
        var setting = await ResolveConversationChannelAsync(conversation, cancellationToken);
        if (setting is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("WhatsApp no está activo y verificado para esta sucursal."));
        var recipient = WhatsAppRecipientResolver.Resolve(conversation);
        if (recipient is null)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("La conversación no tiene un destinatario de WhatsApp válido."));

        var quoted = await ResolveQuotedMessageAsync(conversationId, replyToMessageId, cancellationToken);
        if (replyToMessageId.HasValue && string.IsNullOrWhiteSpace(quoted))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("Ya no puedes citar ese mensaje."));
        var mediaType = asSticker ? WhatsAppMessageType.Sticker : MediaTypeFromContentType(file.ContentType, file.FileName);
        var fileName = SanitizeFileName(file.FileName);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();
        if (asSticker && !IsSupportedSticker(bytes))
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse(UnsupportedSticker));
        var timestamp = _clock.UtcNow;
        var trimmedCaption = asSticker || string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (trimmedCaption?.Length > 4096)
            return BadRequest(ApiResponse<WhatsAppMessageDto>.ErrorResponse("El texto del archivo no puede superar 4096 caracteres."));

        var text = asSticker ? string.Empty : trimmedCaption ?? fileName;

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
            sendResult = quoted is null
                ? await _whatsAppCloudClient.SendMediaMessageAsync(setting.PhoneNumberId, setting.AccessToken, recipient,
                    MediaTypeToApi(mediaType), uploadResult.MediaId, trimmedCaption, fileName, cancellationToken)
                : await _whatsAppCloudClient.SendMediaReplyAsync(setting.PhoneNumberId, setting.AccessToken, recipient,
                    MediaTypeToApi(mediaType), uploadResult.MediaId, trimmedCaption, fileName, quoted, cancellationToken);
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
                _pos_forwarded = forwarded,
                fileName,
                caption = trimmedCaption,
                context = quoted is null ? null : new { message_id = quoted },
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

        await NotifyWhatsAppMessageCreatedAsync(message.Id, cancellationToken, clearPendingResponse: !forwarded || ownsAttention);
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
        {
            _branchContext.EnsureAccess(conversation.BranchId);
            return await _db.WhatsAppBranchSettings.AsNoTracking().ForConversation(conversation)
                .AnyAsync(x => x.IsActive && x.IsVerified, cancellationToken);
        }
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
                .Select(x => new ConversationChannelCredentials(x.PhoneNumberId, x.AccessToken, x.BusinessAccountId))
                .FirstOrDefaultAsync(cancellationToken);
        }
        return await _db.WhatsAppBranchSettings.AsNoTracking()
            .ForConversation(conversation)
            .Where(x => x.IsActive && x.IsVerified)
            .Select(x => new ConversationChannelCredentials(x.PhoneNumberId, x.AccessToken, x.BusinessAccountId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed record ConversationChannelCredentials(string PhoneNumberId, string AccessToken, string BusinessAccountId = "");

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
                .FirstOrDefaultAsync(x => x.BranchId == resolvedBranchId && !x.CoexistenceEnabled && x.IsActive && x.IsVerified, cancellationToken);

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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.ChannelSettingId == null && x.BranchLineId == null
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

    private async Task<bool> IsServiceWindowOpenAsync(WhatsAppConversation conversation, CancellationToken ct)
    {
        var last = conversation.LastCustomerMessageAt ?? await _db.WhatsAppMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversation.Id && !x.IsHistorical && x.Direction == WhatsAppMessageDirection.Inbound)
            .MaxAsync(x => (DateTime?)x.Timestamp, ct);
        return last.HasValue && AsUtc(last.Value).AddHours(24) > _clock.UtcNow;
    }

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
        return new WhatsAppAttentionDto { AttentionRevision = conversation.AttentionRevision, NeedsHumanResponse = conversation.NeedsHumanResponse, ConversationId = conversation.Id, AttentionMode = AttentionModeToApi(conversation.AttentionMode), AttentionReason = WhatsAppAiDiagnosticsMapper.SanitizeTechnicalDetail(reason), AssignedUserId = conversation.AssignedUserId, AssignedUserName = name, AiPausedAt = AsUtc(conversation.AiPausedAt), HumanAssignedAt = AsUtc(conversation.HumanAssignedAt), ClosedAt = AsUtc(conversation.ClosedAt), AttentionModeUpdatedAt = AsUtc(conversation.AttentionModeUpdatedAt), AttentionModeUpdatedByUserId = conversation.AttentionModeUpdatedByUserId };
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
    private string? ClientRequestId => HttpContext?.Items[WhatsAppManualWorkQueue.ClientRequestItem] as string;

    private void RequireKnownOutcome(WhatsAppCloudSendResult result)
    {
        if ((_externalProcessing || ClientRequestId is not null) && result.OutcomeUncertain)
            throw new IOException("WhatsApp send outcome requires reconciliation.");
    }

    private async Task NotifyWhatsAppMessageCreatedAsync(int messageId, CancellationToken ct, bool clearPendingResponse = true)
    {
        if (clearPendingResponse)
        {
            var sent = await _db.WhatsAppMessages.AsNoTracking().SingleAsync(x => x.Id == messageId, ct);
            if (sent.Direction == WhatsAppMessageDirection.Outbound && sent.Status != WhatsAppMessageStatus.Failed)
            {
                var sentAt = AsUtc(sent.Timestamp);
                var query = _db.WhatsAppConversations.Where(x => x.Id == sent.ConversationId
                    && (!x.LastCustomerMessageAt.HasValue || x.LastCustomerMessageAt <= sentAt));
                if (_db.Database.IsRelational()) await query.ExecuteUpdateAsync(x => x.SetProperty(c => c.NeedsHumanResponse, false), ct);
                else
                {
                    var conversation = await query.FirstOrDefaultAsync(ct);
                    if (conversation is not null) { conversation.NeedsHumanResponse = false; await _db.SaveChangesAsync(ct); }
                }
            }
        }
        await _webhookProcessor.NotifyWhatsAppMessageCreatedAsync(messageId, ct);
    }

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
