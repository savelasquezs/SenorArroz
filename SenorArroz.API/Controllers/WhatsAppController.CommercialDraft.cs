using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using SenorArroz.Infrastructure.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    [HttpGet("conversations/{conversationId:int}/commercial-draft")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetCommercialDraft(int conversationId, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var session = await _db.WhatsAppCommerceSessions.AsNoTracking().Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return Ok(ApiResponse<WhatsAppCommercialDraftDto?>.SuccessResponse(session is null ? null : await CommercialDtoAsync(session, conversation, ct)));
    }

    [HttpGet("conversations/{conversationId:int}/commercial-history")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetCommercialHistory(int conversationId, CancellationToken ct)
    {
        var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var history = await _db.WhatsAppCommerceSessions.AsNoTracking().Where(x => x.ConversationId == conversationId)
            .OrderByDescending(x => x.Id).Take(30).Select(x => new { x.Id, x.CorrelationId, x.CommercialStatus, x.OrderId, x.CheckoutId, x.CreatedAt, x.CompletedAt }).ToListAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(history));
    }

    [HttpPost("conversations/{conversationId:int}/commercial-draft")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<IActionResult> StartCommercialDraft(int conversationId, [FromBody] WhatsAppDraftWriteDto request, CancellationToken ct) =>
        ChangeCommercialDraft(conversationId, request, "start", null, ct);

    [HttpPut("conversations/{conversationId:int}/commercial-draft")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<IActionResult> SaveCommercialDraft(int conversationId, [FromBody] WhatsAppDraftWriteDto request, CancellationToken ct) =>
        ChangeCommercialDraft(conversationId, request, "save", null, ct);

    [HttpPost("conversations/{conversationId:int}/commercial-draft/address-choices")]
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto[]>>> SendCommercialAddressChoices(
        int conversationId, [FromBody] WhatsAppDraftWriteDto request, CancellationToken ct)
    {
        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppMessageDto[]>("send-address-choices", conversationId,
                JsonSerializer.SerializeToElement(request), Request, ct);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendCommercialAddressChoicesAsync(conversationId, request, ct);
    }

    [HttpGet("quick-replies/catalog")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetCommercialProductGroups(CancellationToken ct)
    {
        var groups = await WhatsAppCommercialDraft.ChoiceCatalog(_db)
            .GroupBy(x => new { Id = x.CommercialProfileId!.Value, x.CommercialProfile!.Name })
            .Where(x => x.Any(p => p.Active) && (x.Count() > 1 || x.Any(p => p.Category.StorefrontRole == "rice")))
            .Select(x => new { x.Key.Id, x.Key.Name, IsRice = x.Any(p => p.Category.StorefrontRole == "rice") })
            .ToListAsync(ct);
        return Ok(ApiResponse<WhatsAppProductChoiceGroupDto[]>.SuccessResponse(groups
            .OrderByDescending(x => x.IsRice).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new WhatsAppProductChoiceGroupDto(x.Id, x.Name, x.IsRice,
                WhatsAppCommercialDraft.ProductChoiceShortcut(x.Name, x.IsRice))).ToArray()));
    }

    [HttpPost("conversations/{conversationId:int}/commercial-draft/product-choices/{groupId:int}")]
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppMessageDto[]>>> SendCommercialProductChoices(
        int conversationId, int groupId, [FromBody] WhatsAppDraftWriteDto request, CancellationToken ct)
    {
        if (_manualQueue is not null)
            return await _manualQueue.EnqueueAsync<WhatsAppMessageDto[]>("send-product-choices", conversationId,
                JsonSerializer.SerializeToElement(new { groupId, draft = request }), Request, ct);
        _manualOperations.ControllerContext = ControllerContext;
        return await _manualOperations.SendCommercialProductChoicesAsync(conversationId, groupId, request, ct);
    }

    [HttpPost("conversations/{conversationId:int}/commercial-draft/{operation:regex(^(quote|confirm|cancel|note|retry-message)$)}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<IActionResult> CommercialDraftAction(int conversationId, string operation, [FromBody] WhatsAppDraftWriteDto request,
        [FromServices] ISender sender, CancellationToken ct) => ChangeCommercialDraft(conversationId, request, operation, sender, ct);

    private async Task<IActionResult> ChangeCommercialDraft(int conversationId, WhatsAppDraftWriteDto request, string action, ISender? sender, CancellationToken ct)
    {
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            await WhatsAppCommercialDraft.LockAsync(_db, TenantId, conversationId, ct);
            var conversation = await _db.WhatsAppConversations.SingleOrDefaultAsync(x => x.Id == conversationId, ct);
            if (conversation is null) return NotFound();
            if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
            if (!WhatsAppAttentionService.CanWrite(conversation, _currentUser.Id) || conversation.AttentionRevision != request.AttentionVersion)
                return Conflict(ApiResponse<object>.ErrorResponse("La atención cambió. Revisa el responsable antes de continuar."));
            var session = await _db.WhatsAppCommerceSessions.Where(x => x.ConversationId == conversationId)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (action == "start" && session?.Status == "active")
                return Ok(ApiResponse<WhatsAppCommercialDraftDto>.SuccessResponse(await CommercialDtoAsync(session, conversation, ct)));
            if (session is not null && (session.Id != request.SessionId || session.Version != request.ExpectedVersion))
            {
                if (action == "confirm" && session.Id == request.SessionId && session.OrderId.HasValue)
                    return Ok(ApiResponse<WhatsAppCommercialDraftDto>.SuccessResponse(await CommercialDtoAsync(session, conversation, ct)));
                return Conflict(ApiResponse<WhatsAppCommercialDraftDto>.ErrorResponse("El borrador cambió. Recarga y revisa los cambios antes de guardar."));
            }
            if (action == "start")
            {
                if (session?.CheckoutId is not null && session.OrderId is null)
                {
                    var checkout = await _db.StorefrontCheckouts.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == session.CheckoutId, ct);
                    if (checkout is null || checkout.Status == "review_required" || checkout.ExpiresAt > _clock.UtcNow)
                        throw new BusinessException("Revisa el pago pendiente antes de iniciar otro pedido.");
                }
                session = WhatsAppCommercialDraft.New(conversation, _clock.UtcNow);
                WhatsAppCommercialDraft.Write(session, new()
                {
                    BranchId = WhatsAppCommercialDraft.InitialBranch(conversation),
                    CustomerId = conversation.CustomerId,
                    Type = OrderType.Delivery
                });
                _db.WhatsAppCommerceSessions.Add(session);
            }
            else if (session is null) return NotFound();
            else if (action == "retry-message")
            {
                var message = await _db.WhatsAppCommerceOutboxMessages.SingleOrDefaultAsync(x => x.EventKey == $"whatsapp-order-created:{session.OrderId}", ct);
                if (message is not null && WhatsAppCommercialDraft.IsLegacyAdvisorConfirmation(message))
                    throw new BusinessException("El resumen de pedidos de caja se envía manualmente desde el chat.");
                if (message is null || message.Status != "failed" || message.LastError != "provider_rejected_retryable")
                    throw new BusinessException("El envío requiere revisión o ya está pendiente. No se reenviará un mensaje de resultado incierto.");
                message.Status = "pending";
                message.AttemptCount = 0;
                message.NextAttemptAt = _clock.UtcNow;
            }
            else if (action == "note")
            {
                var note = request.Note?.Trim();
                if (string.IsNullOrEmpty(note) || note.Length > 4000) throw new BusinessException("Escribe una nota de hasta 4000 caracteres.");
                var notes = JsonSerializer.Deserialize<List<WhatsAppInternalNote>>(session.InternalNotesJson, WhatsAppCommercialDraft.JsonOptions) ?? [];
                notes.Add(new(_currentUser.Id, note, _clock.UtcNow));
                session.InternalNotesJson = JsonSerializer.Serialize(notes, WhatsAppCommercialDraft.JsonOptions);
                session.Version++;
            }
            else
            {
                if (session.Status != "active")
                {
                    if (action == "confirm" && session.OrderId.HasValue)
                        return Ok(ApiResponse<WhatsAppCommercialDraftDto>.SuccessResponse(await CommercialDtoAsync(session, conversation, ct)));
                    throw new BusinessException("Este intento terminó. Inicia explícitamente un nuevo pedido.");
                }
                if (action == "save")
                {
                    if (request.CommercialStatus is not ("in_progress" or "waiting_customer")) throw new BusinessException("Estado comercial inválido.");
                    if (request.Editor?.GetRawText().Length > 300000) throw new BusinessException("El borrador excede el tamaño permitido.");
                    await ValidateDraftReferencesAsync(request, ct);
                    await ValidateProductSuggestionsAsync(conversation.Id, session, request.Order, ct);
                    WhatsAppCommercialDraft.Write(session, request.Order);
                    session.EditorJson = request.Editor?.GetRawText();
                    session.DraftSource = "advisor";
                    session.CommercialStatus = request.CommercialStatus;
                    session.Version++;
                }
                else if (action == "cancel")
                {
                    conversation.NeedsHumanResponse = false;
                    session.Status = "cancelled";
                    session.CommercialStatus = "no_sale";
                    session.CompletedAt = _clock.UtcNow;
                    session.Version++;
                }
                else
                {
                    var order = WhatsAppCommercialDraft.Read(session, conversation);
                    var quote = await QuoteDraftAsync(session, order, ct);
                    if (action == "quote") return Ok(ApiResponse<WhatsAppDraftQuoteDto>.SuccessResponse(quote));
                    if (quote.Errors.Count > 0) throw new BusinessException(string.Join(" ", quote.Errors));
                    if (request.ReviewedFingerprint is not null && quote.Fingerprint != request.ReviewedFingerprint)
                        return Conflict(ApiResponse<WhatsAppDraftQuoteDto>.ErrorResponse("Revisa nuevamente el pedido: cambiaron sus condiciones."));
                    if (session.CheckoutId is not null) throw new BusinessException("Revisa el pago en línea antes de confirmar manualmente.");
                    if (session.BranchId is null && session.DraftSource == "advisor")
                        WhatsAppCommercialDraft.Write(session, order);
                    order.TakenById = _currentUser.Id;
                    var created = await sender!.Send(new CreateOrderCommand
                    {
                        Order = order, WhatsAppCommerceSessionId = session.Id,
                        ExpectedWhatsAppDraftVersion = session.Version,
                        ExpectedWhatsAppAttentionRevision = conversation.AttentionRevision
                    }, ct);
                    session.OrderId = created.Id;
                    session.Status = "completed";
                    session.CommercialStatus = "order_taken";
                    session.CompletedAt = _clock.UtcNow;
                    session.Version++;
                }
            }
            session!.UpdatedAt = conversation.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            var summary = ToConversationDto(conversation);
            await EnrichCommercialSummariesAsync([summary], ct);
            try { await _whatsAppNotificationService.NotifyAttentionChangedAsync(conversation.BranchId, summary, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Commercial draft saved; realtime notification failed. ConversationId={ConversationId}", conversationId); }
            return Ok(ApiResponse<WhatsAppCommercialDraftDto>.SuccessResponse(await CommercialDtoAsync(session!, conversation, ct)));
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || PostgresExceptionHelper.IsTransactionConflict(ex))
        {
            if (transaction is not null) { await transaction.RollbackAsync(ct); await transaction.DisposeAsync(); }
            if (action is "confirm" or "start")
            {
                var winner = await _db.WhatsAppCommerceSessions.AsNoTracking().Where(x => x.ConversationId == conversationId)
                    .Include(x => x.Conversation).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                if (winner is not null && (action == "start" && winner.Status == "active"
                    || action == "confirm" && winner.Id == request.SessionId && winner.OrderId.HasValue))
                    return Ok(ApiResponse<WhatsAppCommercialDraftDto>.SuccessResponse(await CommercialDtoAsync(winner, winner.Conversation, ct)));
            }
            return Conflict(ApiResponse<object>.ErrorResponse("El pedido o su atención cambiaron. Recarga el borrador y revisa antes de continuar."));
        }
    }

    private async Task ValidateDraftReferencesAsync(WhatsAppDraftWriteDto request, CancellationToken ct)
    {
        var order = request.Order;
        if (order.BranchId > 0 && !await _db.Branches.AnyAsync(x => x.Id == order.BranchId && x.IsActive, ct))
            throw new BusinessException("La sucursal que prepara no está disponible.");
        if (order.CustomerId.HasValue && !await _db.Customers.AnyAsync(x => x.Id == order.CustomerId && x.Active, ct))
            throw new BusinessException("El cliente no pertenece al restaurante.");
        if (order.AddressId.HasValue && !await _db.Addresses.AnyAsync(x => x.Id == order.AddressId && x.CustomerId == order.CustomerId, ct))
            throw new BusinessException("La dirección no pertenece al cliente.");
        if (order.OrderDetails.Count > 200 || order.OrderDetails.Any(x => x.Quantity is < 1 or > 999 || x.UnitPrice < 0 || x.Discount < 0 || (long)x.Discount > (long)x.UnitPrice * x.Quantity))
            throw new BusinessException("Revisa las cantidades, precios y descuentos.");
        var ids = order.OrderDetails.Select(x => x.ProductId).Distinct().ToArray();
        if (await _db.Products.CountAsync(x => ids.Contains(x.Id), ct) != ids.Length)
            throw new BusinessException("Hay productos ajenos al restaurante.");
    }

    private async Task<WhatsAppDraftQuoteDto> QuoteDraftAsync(WhatsAppCommerceSession session, CreateOrderDto order, CancellationToken ct)
    {
        var errors = new List<string>();
        var changes = new List<string>();
        var facts = new List<object>();
        if (order.BranchId <= 0 || !await _db.Branches.AnyAsync(x => x.Id == order.BranchId && x.IsActive, ct)) errors.Add("Selecciona la sucursal que prepara.");
        if (order.OrderDetails.Count == 0) errors.Add("Agrega productos.");
        var ids = order.OrderDetails.Select(x => x.ProductId).Distinct().ToArray();
        var products = await _db.Products.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var availability = _inventory is null || order.BranchId <= 0 ? [] : await _inventory.GetAvailabilityAsync(ids, order.BranchId, ct, true);
        foreach (var item in order.OrderDetails)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.Active) { errors.Add("Hay un producto que ya no está disponible."); continue; }
            facts.Add(new { product.Id, product.Price, product.Active, product.Stock });
            if (product.Price != item.UnitPrice && item.Discount != item.UnitPrice * item.Quantity)
                changes.Add($"{product.Name}: precio de catálogo ${product.Price:N0}; precio seleccionado ${item.UnitPrice:N0}.");
            var stock = availability.FirstOrDefault(x => x.ProductId == item.ProductId);
            if (_inventory is not null && (stock is null || !stock.Available || stock.MaximumQuantity < item.Quantity)) errors.Add($"Revisa la disponibilidad de {product.Name} en destino.");
            facts.Add(new { item.ProductId, stock?.Available, stock?.MaximumQuantity });
        }
        int fee = order.DeliveryFee ?? 0;
        if (order.Type == OrderType.Delivery && (!order.CustomerId.HasValue || !order.AddressId.HasValue)) errors.Add("Selecciona cliente y dirección para el domicilio.");
        if (order.AddressId.HasValue)
        {
            var coverage = await _db.AddressBranches.AsNoTracking().SingleOrDefaultAsync(x => x.AddressId == order.AddressId && x.BranchId == order.BranchId, ct);
            facts.Add(new { coverage?.IsCovered, coverage?.DeliveryFee });
            if (coverage?.IsCovered != true) errors.Add("La dirección no tiene cobertura en la sucursal que prepara.");
            else if (fee != coverage.DeliveryFee) errors.Add($"Actualiza el domicilio a ${coverage.DeliveryFee:N0} para esta sucursal.");
        }
        if ((order.Type != OrderType.Onsite || order.BranchId != _branchContext.EffectiveBranchId) && string.IsNullOrWhiteSpace(order.GuestName)) errors.Add("Indica quién recibe o recoge.");
        if (order.Type == OrderType.Reservation && !order.ReservedFor.HasValue) errors.Add("Selecciona la fecha y hora de entrega.");
        if (order.PrepareAt > order.ReservedFor) errors.Add("La preparación debe ser anterior a la entrega.");
        var bankIds = order.BankPayments.Select(x => x.BankId).Distinct().ToArray();
        if (await _db.Banks.CountAsync(x => bankIds.Contains(x.Id) && x.BranchId == order.BranchId && x.Active, ct) != bankIds.Length) errors.Add("Revisa los bancos de la sucursal que prepara.");
        var appIds = order.AppPayments.Select(x => x.AppId).Distinct().ToArray();
        if (await _db.Apps.CountAsync(x => appIds.Contains(x.Id) && x.Bank.BranchId == order.BranchId && x.Active, ct) != appIds.Length) errors.Add("Revisa los medios de pago de la sucursal que prepara.");
        var total = checked(order.OrderDetails.Sum(x => checked(x.UnitPrice * x.Quantity - x.Discount)) + fee);
        if (order.BankPayments.Sum(x => x.Amount) + order.AppPayments.Sum(x => x.Amount) > total) errors.Add("Los pagos exceden el total.");
        if (order.AppliedBenefitType is not null and not OrderBenefitType.None) changes.Add("El beneficio seleccionado se validará nuevamente al confirmar en la sucursal que prepara.");
        var benefit = await OrderBenefitEligibility.ValidateAsync(_db, order, _currentUser.Role, _clock.UtcNow, ct);
        if (benefit.Error is not null) errors.Add(benefit.Error);
        if (benefit.Snapshot is not null) facts.Add(benefit.Snapshot);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { session.Id, session.Version, order, facts, errors, total }))));
        return new(fingerprint, total, changes, errors);
    }

    private async Task<WhatsAppCommercialDraftDto> CommercialDtoAsync(WhatsAppCommerceSession session, WhatsAppConversation conversation, CancellationToken ct)
    {
        var message = session.OrderId.HasValue ? await _db.WhatsAppCommerceOutboxMessages.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EventKey == $"whatsapp-order-created:{session.OrderId}", ct) : null;
        if (message is not null && WhatsAppCommercialDraft.IsLegacyAdvisorConfirmation(message)) message = null;
        var commercialStatus = session.CommercialStatus;
        if (session.CheckoutId is not null && session.OrderId is null)
        {
            var checkout = await _db.StorefrontCheckouts.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == session.CheckoutId, ct);
            if (checkout is not null && checkout.Status != "review_required" && checkout.ExpiresAt <= _clock.UtcNow) commercialStatus = "no_sale";
        }
        return new(session.Id, session.CorrelationId, session.ConversationId, session.Version, session.Status, commercialStatus,
            WhatsAppCommercialDraft.Read(session, conversation), session.EditorJson is null ? null : JsonSerializer.Deserialize<JsonElement>(session.EditorJson),
            session.OrderId, session.CheckoutId, message?.Status, message?.LastError,
            JsonSerializer.Deserialize<List<WhatsAppInternalNote>>(session.InternalNotesJson, WhatsAppCommercialDraft.JsonOptions) ?? []);
    }
    private async Task EnrichCommercialSummariesAsync(IReadOnlyList<WhatsAppConversationDto> conversations, CancellationToken ct)
    {
        var ids = conversations.Select(x => x.Id).ToArray();
        var latestIds = _db.WhatsAppCommerceSessions.Where(x => ids.Contains(x.ConversationId))
            .GroupBy(x => x.ConversationId).Select(group => group.Max(x => x.Id));
        var summaries = await _db.WhatsAppCommerceSessions.AsNoTracking().Where(x => latestIds.Contains(x.Id))
            .Select(x => new { x.ConversationId, x.CommercialStatus, x.OrderId }).ToDictionaryAsync(x => x.ConversationId, ct);
        foreach (var conversation in conversations)
        {
            var summary = summaries.GetValueOrDefault(conversation.Id);
            conversation.CommercialStatus = summary?.CommercialStatus ?? "not_started";
            conversation.CommercialOrderId = summary?.OrderId;
        }
    }

}
