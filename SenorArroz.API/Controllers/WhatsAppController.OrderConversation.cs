using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
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

    [HttpGet("orders/{orderId:int}/deliveryman-contact")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<WhatsAppDeliverymanContactDto>>> GetOrderDeliverymanContact(
        int orderId, [FromQuery] int conversationId, [FromServices] OrderManagementService orders, CancellationToken ct)
    {
        var order = await orders.RequireOrder(orderId, ct);
        var source = await GetAccessibleConversationsQuery().SingleOrDefaultAsync(c => c.Id == conversationId
            && c.Status != WhatsAppConversationStatus.Archived, ct);
        if (source is null || (order.WhatsAppConversationId is int linkedId
                ? linkedId != source.Id
                : !await _db.WhatsAppCommerceSessions.AnyAsync(s => s.TenantId == TenantId
                    && s.ConversationId == source.Id && s.OrderId == order.Id, ct)))
            return NotFound();

        if (order.Type != OrderType.Delivery || order.Status != OrderStatus.OnTheWay || order.DeliveryManId is not int driverId)
            return ContactUnavailable("El pedido debe estar en camino y tener un domiciliario asignado.");

        var driver = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == driverId && u.Active
            && u.TenantId == TenantId && u.BranchId == order.BranchId, ct);
        if (driver is null || !driver.CanDeliver)
            return ContactUnavailable("El domiciliario asignado ya no está disponible.");

        var workPhone = await (from route in _db.DeliveryRoutes.AsNoTracking()
            join session in _db.DeliveryWorkSessions.AsNoTracking() on route.WorkSessionId equals (int?)session.Id
            join phone in _db.DeliveryWorkPhones.AsNoTracking()
                on new { session.TenantId, session.BranchId, session.DeviceInstallationId }
                equals new { phone.TenantId, phone.BranchId, phone.DeviceInstallationId }
            where route.Id == order.DeliveryRouteId && route.TenantId == TenantId && route.BranchId == order.BranchId
                && route.DeliverymanId == driverId && session.TenantId == route.TenantId && session.BranchId == route.BranchId
                && session.DeliverymanId == driverId
            select phone.Phone).SingleOrDefaultAsync(ct);
        var personal = WhatsAppOperationalContacts.Normalize(driver.Phone);
        var candidates = new[] { workPhone, personal }.OfType<string>().Distinct().ToArray();
        var resolved = await WhatsAppOperationalContacts.ResolveAsync(_db, TenantId, candidates, _clock.UtcNow, ct);
        var number = candidates.FirstOrDefault(n => resolved.GetValueOrDefault(n) is
            { Kind: WhatsAppOperationalContacts.Deliveryman, Id: var id } && id == driverId);
        if (number is null)
            return ContactUnavailable("El domiciliario no tiene un número de WhatsApp disponible.");

        var chats = await WhatsAppOperationalContacts.WithPhones(GetAccessibleConversationsQuery(), [number])
            .Where(c => c.Id != source.Id && c.Status != WhatsAppConversationStatus.Archived)
            .Select(c => new { c.Id, SameLine = source.ChannelSettingId.HasValue
                ? c.ChannelSettingId == source.ChannelSettingId
                : c.ChannelSettingId == null && c.BranchId == source.BranchId && c.BranchLineId == source.BranchLineId })
            .OrderByDescending(c => c.SameLine).ThenBy(c => c.Id).Take(2).ToListAsync(ct);
        var uniqueChat = chats.Count == 1 || (chats.Count == 2 && chats[0].SameLine && !chats[1].SameLine);
        var shareAllowed = WhatsAppAttentionService.CanWrite(source, _currentUser.Id)
            && await _manualOperations.IsServiceWindowOpenAsync(source, ct);
        if (!await _db.Orders.AsNoTracking().AnyAsync(o => o.TenantId == TenantId && o.Id == orderId
            && o.ManagementVersion == order.ManagementVersion && o.Status == OrderStatus.OnTheWay
            && o.DeliveryManId == driverId && o.DeliveryRouteId == order.DeliveryRouteId, ct))
            return ContactUnavailable("El pedido cambió. Actualiza el pedido antes de consultar el contacto.");

        return Ok(ApiResponse<WhatsAppDeliverymanContactDto>.SuccessResponse(new(
            ConversationId: uniqueChat ? chats[0].Id : null,
            ShareText: shareAllowed ? $"Puedes contactar a {driver.Name}, el domiciliario de tu pedido #{orderId}, por WhatsApp: https://wa.me/{number}" : null,
            ChatUnavailableReason: uniqueChat ? null : chats.Count == 0
                ? "El domiciliario no tiene una conversación disponible en esta bandeja."
                : "Hay varias conversaciones para el domiciliario. Selecciona la correcta en la bandeja.",
            ShareUnavailableReason: shareAllowed ? null : "Toma la conversación y verifica que la ventana de atención esté abierta.")));
    }

    private OkObjectResult ContactUnavailable(string message) =>
        Ok(ApiResponse<WhatsAppDeliverymanContactDto>.SuccessResponse(new(ChatUnavailableReason: message, ShareUnavailableReason: message)));
}
