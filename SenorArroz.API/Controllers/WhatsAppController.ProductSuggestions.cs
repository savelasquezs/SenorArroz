using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.Inventory.DTOs;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    private const int ProductSuggestionBatchSize = 10;

    [HttpPost("conversations/{conversationId:int}/product-suggestions")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> GetProductSuggestions(int conversationId, [FromBody] WhatsAppProductSuggestionsRequest request, CancellationToken ct)
    {
        if (request.MessageIds is not { Length: > 0 and <= ProductSuggestionBatchSize } || request.MessageIds.Any(id => id <= 0))
            return BadRequest(ApiResponse<object>.ErrorResponse("Selecciona entre uno y diez mensajes."));

        var conversation = await _db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
        var session = await _db.WhatsAppCommerceSessions.AsNoTracking().Where(x => x.ConversationId == conversationId && x.Status == "active")
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var order = session is null ? new CreateOrderDto { BranchId = WhatsAppCommercialDraft.InitialBranch(conversation) }
            : WhatsAppCommercialDraft.Read(session, conversation);
        var matches = await MatchProductSuggestionsAsync(conversationId, request.MessageIds, ct);
        var available = await ProductSuggestionAvailabilityAsync(
            matches.Values.SelectMany(list => list).Where(m => m.Product is not null).Select(m => m.Product!.Id), order.BranchId, ct);
        var quantities = order.OrderDetails.ToLookup(x => x.ProductId);
        var messages = matches.Select(pair => new WhatsAppMessageProductSuggestionsDto(pair.Key, pair.Value
            .Select(m => new WhatsAppProductSuggestionDto(m.Clarification ? "clarify" : m.Reply is not null ? "reply" : "add",
                m.Product?.Id, m.Product?.Name, m.Quantity, m.Product?.Price,
                m.Product is not null && order.BranchId > 0 && CanAddSuggestedProduct(available, m.Product.Id,
                    quantities[m.Product.Id].Sum(x => (long)x.Quantity) + m.Quantity), m.Reply)).ToArray())).ToArray();
        return Ok(ApiResponse<WhatsAppProductSuggestionsDto>.SuccessResponse(new(order.BranchId, messages)));
    }

    private async Task<Dictionary<int, IReadOnlyList<WhatsAppProductSuggestions.Match>>> MatchProductSuggestionsAsync(
        int conversationId, int[] ids, CancellationToken ct)
    {
        var messages = await _db.WhatsAppMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && ids.Contains(x.Id) && x.Direction == WhatsAppMessageDirection.Inbound
                && x.Type == WhatsAppMessageType.Text && !x.IsHistorical)
            .Select(x => new { x.Id, x.TextBody, CartPayload = x.AiProcessingError == WhatsAppCatalogCart.ManualReview
                ? x.RawPayload : null }).ToListAsync(ct);
        if (messages.Count == 0) return [];
        var catalog = await _db.Products.AsNoTracking()
            .Where(x => x.Active && StorefrontCommerceService.PublicRoles.Contains(x.Category.StorefrontRole))
            .Select(p => new WhatsAppProductSuggestions.CatalogProduct(p.Id, p.Name,
                p.CommercialProfile == null ? null : p.CommercialProfile.Name, p.Category.StorefrontRole, p.StorefrontVariantLabel,
                p.ServesPeopleMin, p.ServesPeopleMax, p.Price)).ToListAsync(ct);
        var products = catalog.ToDictionary(p => p.Id);
        return messages.ToDictionary(m => m.Id, m => m.CartPayload is null
            ? WhatsAppProductSuggestions.Find(m.TextBody, catalog)
            : WhatsAppCatalogCart.Read(m.CartPayload) is { } cart ? MatchCatalogCart(cart, products) : []);
    }

    private IReadOnlyList<WhatsAppProductSuggestions.Match> MatchCatalogCart(WhatsAppCatalogCart cart,
        IReadOnlyDictionary<int, WhatsAppProductSuggestions.CatalogProduct> catalog)
        => cart.Items.Select(item => (Id: cart.ProductId(_whatsAppOptions, item), item.Quantity))
            .Where(item => item.Id.HasValue && catalog.ContainsKey(item.Id.Value))
            .GroupBy(item => item.Id!.Value, item => (long)item.Quantity)
            .Select(group => (group.Key, Quantity: group.Sum()))
            .Where(item => item.Quantity <= 999)
            .Select(item => new WhatsAppProductSuggestions.Match(catalog[item.Key], (int)item.Quantity, null))
            .ToArray();

    private async Task<IReadOnlyDictionary<int, ProductAvailabilityDto>?> ProductSuggestionAvailabilityAsync(
        IEnumerable<int> ids, int branchId, CancellationToken ct)
    {
        if (_inventory is null) return null;
        var products = ids.Distinct().ToArray();
        if (branchId <= 0 || products.Length == 0) return new Dictionary<int, ProductAvailabilityDto>();
        return (await _inventory.GetAvailabilityAsync(products, branchId, ct, true)).ToDictionary(x => x.ProductId);
    }

    private static bool CanAddSuggestedProduct(IReadOnlyDictionary<int, ProductAvailabilityDto>? availability, int productId, long total)
        => total is > 0 and <= int.MaxValue && (availability is null || availability.TryGetValue(productId, out var product)
            && product.Available && (!product.MaximumQuantity.HasValue || product.MaximumQuantity.Value >= total));

    private async Task ValidateProductSuggestionsAsync(int conversationId, WhatsAppCommerceSession session, CreateOrderDto order, CancellationToken ct)
    {
        var selected = order.OrderDetails.Where(i => i.WhatsAppSuggestionMessageId.HasValue).ToArray();
        if (selected.Length == 0) return;
        if (selected.GroupBy(i => (i.WhatsAppSuggestionMessageId, i.ProductId)).Any(group => group.Count() > 1))
            throw new BusinessException("Una sugerencia solo se puede agregar una vez al borrador.");

        var previous = WhatsAppCommercialDraft.Read(session).OrderDetails
            .Where(i => i.WhatsAppSuggestionMessageId.HasValue).Select(i => (i.WhatsAppSuggestionMessageId, i.ProductId)).ToHashSet();
        var additions = selected.Where(i => !previous.Contains((i.WhatsAppSuggestionMessageId, i.ProductId))).ToArray();
        if (additions.Length == 0) return;
        if (order.BranchId <= 0) throw new BusinessException("Elige la sucursal que prepara el pedido.");
        var matches = await MatchProductSuggestionsAsync(conversationId, additions.Select(i => i.WhatsAppSuggestionMessageId!.Value).Distinct().ToArray(), ct);
        foreach (var item in additions)
        {
            if (!matches.TryGetValue(item.WhatsAppSuggestionMessageId!.Value, out var options)
                || !options.Any(m => m.Reply is null && m.Product?.Id == item.ProductId && m.Quantity == item.Quantity
                    && m.Product.Price == item.UnitPrice))
                throw new BusinessException("La sugerencia o su precio cambió. Vuelve a consultar el mensaje.");
        }
        var available = await ProductSuggestionAvailabilityAsync(additions.Select(i => i.ProductId), order.BranchId, ct);
        var totals = order.OrderDetails.ToLookup(i => i.ProductId);
        if (additions.Any(i => !CanAddSuggestedProduct(available, i.ProductId, totals[i.ProductId].Sum(x => (long)x.Quantity))))
            throw new BusinessException("No hay inventario suficiente para la cantidad solicitada en la sucursal que prepara.");
    }
}
