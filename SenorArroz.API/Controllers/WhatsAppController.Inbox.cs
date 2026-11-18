using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    private sealed record InboxCursor(string Scope, int Priority, DateTime Activity, int Id);

    [HttpGet("conversations/page")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<WhatsAppConversationPageDto>>> GetConversationPage(
        [FromQuery] WhatsAppConversationSearchDto search, CancellationToken cancellationToken)
    {
        var accessibleConversations = GetAccessibleConversationsQuery();
        var isSuperadmin = Roles.IsSuperadmin(_currentUser.Role);
        var adminBranchId = _currentUser.BranchId;
        if (search.BranchId.HasValue && !isSuperadmin && search.BranchId != adminBranchId)
            return Forbid();

        IQueryable<WhatsAppConversation> query = accessibleConversations;
        if (search.Status != "archived") query = query.Where(x => x.Status != WhatsAppConversationStatus.Archived);

        var day = InboxDayStart();
        query = ScopedInbox(query, search.BranchId, search.UnroutedOnly == true);

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
        if (!string.IsNullOrEmpty(search.Label))
        {
            var state = InboxLabelStates(query, day);
            switch (search.Label)
            {
                case SupplierLabel: query = query.Where(x => x.IsSupplier); break;
                case PaymentLabel: query = query.Where(x => x.IsPaymentPending); break;
                case PendingLabel: query = state.Where(x => x.Pending).Select(x => x.Conversation); break;
                case TakenLabel: query = state.Where(x => x.Taken).Select(x => x.Conversation); break;
                default:
                    if (!search.Label.StartsWith(BranchLabelPrefix, StringComparison.Ordinal)
                        || !int.TryParse(search.Label.AsSpan(BranchLabelPrefix.Length), out var target) || target <= 0)
                        return BadRequest(ApiResponse<WhatsAppConversationPageDto>.ErrorResponse("Etiqueta no válida."));
                    query = state.Where(x => x.RemoteBranchId == target).Select(x => x.Conversation);
                    break;
            }
        }
        if (!string.IsNullOrWhiteSpace(search.Inbox))
        {
            if (search.Inbox is not ("free" or "mine" or "team" or "closed"))
                return BadRequest(ApiResponse<WhatsAppConversationPageDto>.ErrorResponse("Bandeja no válida."));
            query = search.Inbox switch
            {
                "closed" => query.Where(x => x.AttentionMode == WhatsAppAttentionMode.Closed),
                "mine" => query.Where(x => x.AttentionMode != WhatsAppAttentionMode.Closed && x.AssignedUserId == _currentUser.Id),
                "team" => query.Where(x => x.AttentionMode != WhatsAppAttentionMode.Closed && x.AssignedUserId != null && x.AssignedUserId != _currentUser.Id),
                _ => query.Where(x => x.AttentionMode != WhatsAppAttentionMode.Closed && x.AssignedUserId == null)
            };
        }

        if (search.Limit is < 1 or > 100)
            return BadRequest(ApiResponse<WhatsAppConversationPageDto>.ErrorResponse("Consulta entre 1 y 100 conversaciones por página."));
        var scope = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            TenantId, Actor = _currentUser.Id, WorkContext = _branchContext.EffectiveBranchId, _currentUser.Role, WorkBranchId = _currentUser.BranchId,
            search.Inbox, search.UnroutedOnly, search.BranchId, search.Search, search.Status, search.UnreadOnly, search.Label, search.Limit
        })));
        InboxCursor? cursor = null;
        if (search.Cursor is not null)
        {
            try
            {
                if (search.Cursor.Length > 512) throw new FormatException();
                cursor = JsonSerializer.Deserialize<InboxCursor>(Convert.FromBase64String(search.Cursor));
                if (cursor is null || cursor.Scope != scope || cursor.Id <= 0 || cursor.Priority is < 0 or > 2
                    || cursor.Activity.Kind != DateTimeKind.Utc) throw new FormatException();
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            { return BadRequest(ApiResponse<WhatsAppConversationPageDto>.ErrorResponse("El cursor no corresponde a esta bandeja o usuario. Actualiza la lista.")); }
        }
        var ranked = query.Select(x => new
        {
            Conversation = x,
            Commerce = _db.WhatsAppCommerceSessions.Where(s => s.ConversationId == x.Id).OrderByDescending(s => s.Id)
                .Select(s => new { s.CommercialStatus, s.OrderId }).FirstOrDefault()
        }).Select(x => new
        {
            x.Conversation.Id,
            Activity = x.Conversation.LastMessageAt ?? x.Conversation.CreatedAt,
            Priority = x.Conversation.NeedsHumanResponse || x.Conversation.UnreadCount > 0
                || x.Commerce != null && x.Commerce.CommercialStatus == "in_progress" ? 0
                : x.Commerce != null && x.Commerce.CommercialStatus == "waiting_customer" ? 1 : 2,
            CommercialStatus = x.Commerce != null ? x.Commerce.CommercialStatus : "not_started",
            OrderId = x.Commerce != null ? x.Commerce.OrderId : null
        });
        if (cursor is not null)
            ranked = ranked.Where(x => x.Priority > cursor.Priority || x.Priority == cursor.Priority
                && (x.Activity < cursor.Activity || x.Activity == cursor.Activity && x.Id < cursor.Id));
        var rows = await ranked.OrderBy(x => x.Priority).ThenByDescending(x => x.Activity).ThenByDescending(x => x.Id)
            .Take(search.Limit + 1).ToListAsync(cancellationToken);
        var more = rows.Count > search.Limit;
        var page = rows.Take(search.Limit).ToArray();
        var ids = page.Select(x => x.Id).ToArray();
        var details = await query.Where(x => ids.Contains(x.Id)).Select(WhatsAppConversationMapper.Projection)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var items = page.Where(row => details.ContainsKey(row.Id)).Select(row =>
        {
            var dto = details[row.Id];
            dto.CommercialStatus = row.CommercialStatus;
            dto.CommercialOrderId = row.OrderId;
            dto.AttentionReason = WhatsAppAiDiagnosticsMapper.SanitizeTechnicalDetail(dto.AttentionReason);
            return dto;
        }).ToArray();
        await EnrichInboxLabelsAsync(items, day, cancellationToken);
        var last = page.LastOrDefault();
        var next = more && last is not null ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new InboxCursor(scope, last.Priority, DateTime.SpecifyKind(last.Activity, DateTimeKind.Utc), last.Id))) : null;
        return Ok(ApiResponse<WhatsAppConversationPageDto>.SuccessResponse(new(items, next, more)));
    }
}
