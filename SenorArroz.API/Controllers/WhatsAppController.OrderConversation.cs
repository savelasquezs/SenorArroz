using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;
using static SenorArroz.API.Services.WhatsAppManualOperations;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    [HttpGet("orders/{orderId:int}/conversation")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<WhatsAppOrderConversationDto>>> GetOrderConversation(
        int orderId, [FromServices] OrderManagementService orders, CancellationToken ct)
    {
        var order = await orders.RequireOrder(orderId, ct);
        if (order.WhatsAppConversationId is int linkedId)
        {
            var linked = await _db.WhatsAppConversations.AsNoTracking()
                .SingleOrDefaultAsync(c => c.TenantId == TenantId && c.Id == linkedId, ct);
            try
            {
                if (linked is not null && await CanAccessConversationAsync(linked, ct))
                    return OrderConversation(new("existing", ConversationId: linkedId));
            }
            catch (Exception ex) when (ex is BranchAccessDeniedException or BranchScopeMismatchException)
            {
                // A remote order grants access to the order, not to a branch's WhatsApp line.
            }
            return OrderConversation(new("unavailable"), "La conversación vinculada no está disponible desde esta sucursal.");
        }

        var contact = await _db.Customers.AsNoTracking()
            .Where(c => c.TenantId == TenantId && c.Id == order.CustomerId)
            .Select(c => new { c.Phone1, c.WhatsAppUsername }).SingleOrDefaultAsync(ct);
        var phone = NormalizeStarterPhone(contact?.Phone1);
        var username = phone is null ? WhatsAppIdentityNormalizer.NormalizeUsername(contact?.WhatsAppUsername) : null;
        if (phone is null && username is null)
            return OrderConversation(new("unavailable"), "El pedido no tiene un contacto válido para WhatsApp.");

        var conversations = GetAccessibleConversationsQuery();
        if (phone is not null)
        {
            var internationalPhone = $"+{phone}";
            var localPhone = ColombianPhoneNormalizer.TryNormalize(phone, out var mobile) ? mobile : phone;
            conversations = conversations.Where(c => c.PhoneNumber == phone || c.PhoneNumber == internationalPhone
                || c.PhoneNumber == localPhone);
        }
        else
        {
            conversations = conversations.Where(c => c.WhatsAppUsername == username);
        }

        var matches = await conversations.Select(c => c.Id).Take(2).ToListAsync(ct);
        if (matches.Count == 1)
            return OrderConversation(new("existing", ConversationId: matches[0]));
        if (matches.Count > 1)
            return OrderConversation(new("ambiguous"), "Hay varias conversaciones para este contacto. Selecciona la correcta en la bandeja de WhatsApp.");

        var branchId = _branchContext.RequireBranch();
        if (phone is not null && await _manualOperations.CanStartOrderConversationAsync(branchId, phone, ct))
            return OrderConversation(new("start", Phone: $"+{phone}", BranchId: branchId));

        return OrderConversation(new("unavailable"), phone is null
            ? "No hay una conversación disponible para este usuario de WhatsApp."
            : "No se puede iniciar una conversación con este teléfono desde la sucursal de trabajo.");
    }

    private OkObjectResult OrderConversation(WhatsAppOrderConversationDto data, string message = "OK") =>
        Ok(ApiResponse<WhatsAppOrderConversationDto>.SuccessResponse(data, message));
}
