using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Application.Features.Suppliers.Queries;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    private async Task<Dictionary<string, WhatsAppOperationalContacts.Match>> InboxQuickContactsAsync(
        bool suppliersOnly, DateTime now, DateTime day, CancellationToken ct)
    {
        var tenantId = TenantId;
        int[] deliverymanIds = [];
        List<string> candidates;
        if (suppliersOnly)
        {
            candidates = await _db.Suppliers.AsNoTracking().Where(s => s.TenantId == tenantId)
                .Select(s => s.Phone).ToListAsync(ct);
        }
        else
        {
            var branch = _branchContext.RequireBranch();
            var end = ColombiaTimeHelper.GetColombiaStartOfTomorrowUtcFromUtc(now);
            if (_db.Database.IsRelational())
            {
                deliverymanIds = await _db.Database.SqlQuery<int>($$"""
                    SELECT DISTINCT o.delivery_man_id AS "Value"
                    FROM public."order" o
                    WHERE o.tenant_id = {{tenantId}} AND (o.branch_id = {{branch}} OR o.origin_branch_id = {{branch}})
                      AND o.delivery_man_id IS NOT NULL AND o.type = 'delivery' AND o.status <> 'cancelled'
                      AND CASE WHEN pg_input_is_valid(o.status_times ->> 'delivery_man_assigned', 'timestamp with time zone')
                        AND (o.status_times ->> 'delivery_man_assigned') ~ '[Tt ].*([Zz]|[+-][0-9]{2}:[0-9]{2})$'
                        THEN (o.status_times ->> 'delivery_man_assigned')::timestamptz <@ tstzrange({{day}}, {{end}}, '[)')
                        ELSE false END
                    """).ToArrayAsync(ct);
            }
            else
            {
                var orders = await _db.Orders.AsNoTracking().Where(o => o.TenantId == tenantId
                    && (o.BranchId == branch || o.OriginBranchId == branch) && o.DeliveryManId != null
                    && o.Type == OrderType.Delivery && o.Status != OrderStatus.Cancelled).ToListAsync(ct);
                deliverymanIds = orders.Where(o => o.GetStatusTimes().TryGetValue(Order.DeliveryManAssignedStatusTimeKey, out var at)
                        && at >= day && at < end)
                    .Select(o => o.DeliveryManId!.Value).Distinct().ToArray();
            }

            if (deliverymanIds.Length == 0) return [];
            candidates = await _db.Users.AsNoTracking().Where(u => u.TenantId == tenantId && deliverymanIds.Contains(u.Id))
                .Select(u => u.Phone).ToListAsync(ct);
            candidates.AddRange(await _db.DeliveryWorkPhones.AsNoTracking().Where(p => p.TenantId == tenantId)
                .Select(p => p.Phone).ToListAsync(ct));
        }

        var numbers = candidates.Select(WhatsAppOperationalContacts.Normalize).OfType<string>().Distinct().ToArray();
        var resolved = await WhatsAppOperationalContacts.ResolveAsync(_db, tenantId, numbers, now, ct);
        return resolved.Where(x => suppliersOnly ? x.Value.Kind == WhatsAppOperationalContacts.Supplier
                : x.Value.Kind == WhatsAppOperationalContacts.Deliveryman && deliverymanIds.Contains(x.Value.Id))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    private async Task<OkObjectResult> OperationalContactResponse(WhatsAppConversation conversation, string message, CancellationToken ct)
    {
        var dto = ToConversationDto(conversation);
        await WhatsAppOperationalContacts.EnrichAsync(_db, conversation.TenantId, [dto], ct, _clock.UtcNow);
        return Ok(ApiResponse<WhatsAppConversationDto>.SuccessResponse(dto, message));
    }

    [HttpGet("conversations/{conversationId:int}/delivery-routes")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetConversationDeliveryRoutes(int conversationId, CancellationToken ct, int? expectedDeliverymanId = null)
    {
        var conversation = await GetAccessibleConversationsQuery().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        var now = _clock.UtcNow;
        var contact = await WhatsAppOperationalContacts.FindForRoutesAsync(_db, conversation, now, ct);
        if (contact is not { Kind: WhatsAppOperationalContacts.Deliveryman, BranchId: int driverBranch }) return NotFound();
        if (contact.Id == 0 || (contact.DeviceInstallationId is not null
            && expectedDeliverymanId.HasValue && contact.Id != expectedDeliverymanId))
            return Ok(ApiResponse<IReadOnlyList<WhatsAppRecentDeliveryRouteDto>>.SuccessResponse([]));

        var branch = _branchContext.RequireBranch();
        var orders = _db.Orders.AsNoTracking().Where(o => o.TenantId == TenantId
            && (o.BranchId == branch || o.OriginBranchId == branch));
        var stops = _db.DeliveryRouteStops.AsNoTracking().Where(s => s.TenantId == TenantId
            && orders.Any(o => o.Id == s.OrderId));
        var currentId = await DeliveryRouteScope.Current(_db, driverBranch, contact.Id, now)
            .Where(r => r.TenantId == TenantId)
            .Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (!currentId.HasValue)
        {
            var today = ColombiaTimeHelper.GetTodayStartInUtcFromUtc(now);
            currentId = await _db.DeliveryRoutes.AsNoTracking()
                .Where(r => r.TenantId == TenantId && r.BranchId == driverBranch && r.DeliverymanId == contact.Id
                    && r.WorkSessionId == null && r.CompletedAtUtc == null
                    && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress)
                    && r.LastAssignmentAtUtc >= today && r.LastAssignmentAtUtc <= now && r.Stops.Any())
                .OrderByDescending(r => r.LastAssignmentAtUtc).ThenByDescending(r => r.Id)
                .Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        }

        var routes = await _db.DeliveryRoutes.AsNoTracking()
            .Where(r => r.TenantId == TenantId && r.DeliverymanId == contact.Id && r.Status != DeliveryRouteStatus.Cancelled
                && stops.Any(s => s.DeliveryRouteId == r.Id)
                && (r.Id == currentId || r.DepartedAtUtc != null || r.Status == DeliveryRouteStatus.Completed
                    || r.Stops.Any(s => s.Order.DeliveryRouteId == r.Id && s.Order.Status == OrderStatus.Delivered)))
            .OrderByDescending(r => r.Id == currentId)
            .ThenByDescending(r => r.DepartedAtUtc ?? r.RouteStartedAtUtc ?? r.LastAssignmentAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(2)
            .Select(r => new WhatsAppRecentDeliveryRouteDto(r.Id, r.Id == currentId,
                r.DepartedAtUtc ?? r.RouteStartedAtUtc ?? r.LastAssignmentAtUtc,
                stops.Where(s => s.DeliveryRouteId == r.Id).OrderBy(s => s.StopSequence).ThenBy(s => s.OrderId)
                    .Select(s => new WhatsAppDeliveryRouteOrderDto(s.OrderId, s.StopSequence, s.Order.Status)).ToList()))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<WhatsAppRecentDeliveryRouteDto>>.SuccessResponse(routes));
    }

    [HttpGet("conversations/{conversationId:int}/supplier-items")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetConversationSupplierItems(int conversationId, [FromServices] ISender sender, CancellationToken ct)
    {
        var conversation = await GetAccessibleConversationsQuery().SingleOrDefaultAsync(x => x.Id == conversationId, ct);
        if (conversation is null) return NotFound();
        var contact = await WhatsAppOperationalContacts.FindAsync(_db, conversation, ct);
        if (contact is not { Kind: WhatsAppOperationalContacts.Supplier }) return NotFound();

        var items = await sender.Send(new GetSupplierExpensesQuery { SupplierId = contact.Id }, ct);
        return Ok(ApiResponse<IReadOnlyList<SupplierExpenseDto>>.SuccessResponse(items));
    }
}
