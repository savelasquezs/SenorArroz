using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    public sealed record ClosedDayPreview(int BranchId, string BranchName, DateOnly? ClosedDate,
        int[] ConversationIds, string? UnavailableReason);

    public sealed record ClosedDayReply(int BranchId, DateOnly ClosedDate, int ConversationId, string? Text);

    private sealed record ClosedDayWindow(DateOnly Date, DateTime StartsAt, DateTime EndsAt)
    {
        public string DispatchPrefix => $"closed-day:{Date:yyyy-MM-dd}:";
    }

    [HttpGet("closed-day-replies")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<ClosedDayPreview>>> PreviewClosedDayReplies(int? branchId, CancellationToken ct)
    {
        var id = _branchContext.RequireBranch(branchId);
        var branchName = await _db.Branches.AsNoTracking().Where(b => b.Id == id && b.IsActive)
            .Select(b => b.Name).SingleOrDefaultAsync(ct);
        if (branchName is null) return NotFound();

        var (window, reason) = await ClosedDayForBranchAsync(id, ct);
        int[] ids = window is null ? [] : await ClosedDayConversations(id, window, true)
            .OrderBy(c => c.LastCustomerMessageAt).ThenBy(c => c.Id).Select(c => c.Id).ToArrayAsync(ct);
        return Ok(ApiResponse<ClosedDayPreview>.SuccessResponse(new(id, branchName, window?.Date, ids, reason)));
    }

    [HttpPost("closed-day-replies")]
    [RequestSizeLimit(32768)]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<string>>> SendClosedDayReply(ClosedDayReply request, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
            return BadRequest(ApiResponse<string>.ErrorResponse("Escribe un mensaje de hasta 4096 caracteres."));

        var branchId = _branchContext.RequireBranch(request.BranchId);
        var (window, reason) = await ClosedDayForBranchAsync(branchId, ct);
        if (window is null || window.Date != request.ClosedDate)
            return Conflict(ApiResponse<string>.ErrorResponse(reason ?? "Vuelve a revisar las conversaciones del día de descanso."));

        var conversation = await ClosedDayConversations(branchId, window, true)
            .SingleOrDefaultAsync(c => c.Id == request.ConversationId, ct);
        if (conversation is null) return Ok(ApiResponse<string>.SuccessResponse("skipped"));

        _manualOperations.ControllerContext = ControllerContext;
        var outcome = await _manualOperations.SendClosedDayReplyAsync(conversation, text,
            window.DispatchPrefix + conversation.Id, token => ClosedDayConversations(branchId, window, false)
                .AnyAsync(c => c.Id == conversation.Id, token), ct);
        return Ok(ApiResponse<string>.SuccessResponse(outcome));
    }

    private async Task<(ClosedDayWindow? Window, string? Reason)> ClosedDayForBranchAsync(int branchId, CancellationToken ct)
    {
        var today = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(_clock.UtcNow);
        if (today.DayOfWeek != DayOfWeek.Thursday) return (null, "Disponible los jueves.");

        var hours = await _businessHoursService.GetBusinessHours(branchId, ct);
        if (!hours.Any(h => h.Id.HasValue && h.DayOfWeek == DayOfWeek.Wednesday && h.IsClosed))
            return (null, "El horario de esta sucursal debe tener el miércoles marcado como cerrado.");
        var endsAt = ColombiaTimeHelper.ConvertColombiaToUtc(today.ToDateTime(TimeOnly.MinValue));
        return (new(today.AddDays(-1), endsAt.AddDays(-1), endsAt), null);
    }

    private IQueryable<WhatsAppConversation> ClosedDayConversations(int branchId, ClosedDayWindow window, bool excludeAttempted)
    {
        var now = _clock.UtcNow;
        var cutoff = now.AddHours(-24);
        var prefix = window.DispatchPrefix;
        var query = _db.WhatsAppConversations.Where(c => c.BranchId == branchId && c.Branch.IsActive
            && c.ChannelSettingId == null && !c.IsSupplier && c.AttentionMode != WhatsAppAttentionMode.Closed
            && c.Status != WhatsAppConversationStatus.Closed && c.Status != WhatsAppConversationStatus.Archived
            && (c.PhoneNumber != null && c.PhoneNumber != "" || c.WhatsAppUserId != null && c.WhatsAppUserId != "")
            && _db.WhatsAppBranchSettings.Any(s => s.BranchId == c.BranchId && s.IsActive && s.IsVerified
                && (c.BranchLineId.HasValue ? s.Id == c.BranchLineId : !s.CoexistenceEnabled)));

        return from c in query
            let inbound = c.Messages.Where(m => m.Direction == WhatsAppMessageDirection.Inbound && !m.IsHistorical)
            let dayContact = inbound.Where(m => m.Timestamp >= window.StartsAt && m.Timestamp < window.EndsAt)
                .Max(m => (DateTime?)m.Timestamp)
            let latest = c.LastCustomerMessageAt ?? inbound.Max(m => (DateTime?)m.Timestamp)
            where now < window.EndsAt.AddDays(1) && dayContact.HasValue && latest > cutoff && latest <= now
                && !c.Messages.Any(m => m.Direction == WhatsAppMessageDirection.Outbound && !m.IsHistorical
                    && m.Timestamp >= dayContact && m.Status != WhatsAppMessageStatus.Failed
                    && (m.AgentDispatchKey == null || !m.AgentDispatchKey.StartsWith("away:")))
                && (!excludeAttempted || !c.Messages.Any(m => m.AgentDispatchKey != null && m.AgentDispatchKey.StartsWith(prefix)))
            select c;
    }
}
