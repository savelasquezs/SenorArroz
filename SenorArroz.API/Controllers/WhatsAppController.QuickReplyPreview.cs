using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    [HttpPost("conversations/{conversationId:int}/quick-replies/{replyId:int}/preview")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> PreviewQuickReply(int conversationId, int replyId,
        [FromBody] WhatsAppQuickReplyPreviewRequest request, CancellationToken ct)
    {
        var chat = await _db.WhatsAppConversations.AsNoTracking().Include(x => x.Customer)
            .SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (chat is null) return NotFound();
        if (!WhatsAppAttentionService.CanWrite(chat, _currentUser.Id) || !await CanAccessConversationAsync(chat, ct))
            return Forbid();

        var replyBranchId = _manualOperations.QuickReplyBranchId(chat);
        var reply = await _db.WhatsAppQuickReplies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == replyId && x.BranchId == replyBranchId && x.IsActive, ct);
        if (reply is null) return NotFound();
        if (reply.ReplyMode != WhatsAppQuickReply.ChicharronQuote)
        {
            var text = WhatsAppManualOperations.RenderQuickReplyTemplate(reply.MessageTemplate, chat).Trim();
            return text.Length is > 0 and <= 4096 ? QuickReplyPreview(new(text))
                : BadRequest(ApiResponse<object>.ErrorResponse("Revisa el contenido de la respuesta rápida."));
        }

        var catalog = await ReadProductSuggestionCatalogAsync(ct);
        var products = catalog.Select(x => (Product: x, Presentation: WhatsAppProductSuggestions.ChicharronPresentation(x)))
            .Where(x => x.Presentation.HasValue).Select(x => (x.Product, Key: x.Presentation!.Value)).ToArray();
        var choices = products.Select(x => new WhatsAppQuickReplyPresentationDto(x.Key.Family, x.Key.Size))
            .Distinct().ToArray();
        if (choices.Length == 0)
            return BadRequest(ApiResponse<object>.ErrorResponse("El catálogo no tiene presentaciones de Paisa o Ropa Vieja para cotizar."));

        var latest = request is { Family: not null, Size: not null } ? null : await _db.WhatsAppMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.Direction == WhatsAppMessageDirection.Inbound && !x.IsHistorical)
            .OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id)
            .Select(x => x.Type == WhatsAppMessageType.Text ? x.TextBody : null).FirstOrDefaultAsync(ct);
        var (inferredFamily, inferredSize) = WhatsAppProductSuggestions.ChicharronContext(latest, catalog);
        var family = request.Family ?? inferredFamily;
        var size = request.Size ?? inferredSize;
        if (family is not null && !choices.Any(x => x.Family == family)) family = null;
        if (size is not null && !choices.Any(x => x.Size == size && (family is null || x.Family == family))) size = null;
        if (family is null || size is null) return QuickReplyPreview(new(null, family, size, choices));

        var variants = products.Where(x => x.Key.Family == family && x.Key.Size == size).ToArray();
        if (variants.GroupBy(x => x.Key.WithChicharron).Any(x => x.Count() > 1))
            return Conflict(ApiResponse<object>.ErrorResponse("Hay presentaciones duplicadas. Revisa el catálogo para cotizar este arroz."));

        var session = await _db.WhatsAppCommerceSessions.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.Status == "active")
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var branchId = session is null ? WhatsAppCommercialDraft.InitialBranch(chat) : WhatsAppCommercialDraft.Read(session, chat).BranchId;
        if (branchId <= 0)
            return BadRequest(ApiResponse<object>.ErrorResponse("Selecciona la sucursal que prepara el pedido para cotizar."));

        var availability = await ProductSuggestionAvailabilityAsync(variants.Select(x => x.Product.Id), branchId, ct);
        var culture = CultureInfo.GetCultureInfo("es-CO");
        string Price(bool withChicharron)
        {
            var product = variants.Where(x => x.Key.WithChicharron == withChicharron).Select(x => x.Product).SingleOrDefault();
            return product is not null && CanAddSuggestedProduct(availability, product.Id, 1)
                ? $"$ {product.Price.ToString("N0", culture)}" : "No disponible";
        }

        var familyName = family == "paisa" ? "Paisa" : "Ropa Vieja";
        var sizeName = size switch { "duo" => "Dúo", "trio" => "Trío", "super" => "Súper", _ => culture.TextInfo.ToTitleCase(size) };
        var quote = $"Arroz {familyName} {sizeName}\nCon chicharrón: {Price(true)}\nSin chicharrón: {Price(false)}";
        return QuickReplyPreview(new(quote, family, size));
    }

    private IActionResult QuickReplyPreview(WhatsAppQuickReplyPreviewDto preview) =>
        Ok(ApiResponse<WhatsAppQuickReplyPreviewDto>.SuccessResponse(preview));
}
