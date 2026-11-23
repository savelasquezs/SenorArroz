using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

public partial class WhatsAppController
{
    private const string SupplierLabel = "supplier";
    private const string PendingLabel = "pending";
    private const string TakenLabel = "taken";
    private const string PaymentLabel = "payment_pending";
    private const string BranchLabelPrefix = "branch:";
    private const string CustomLabelPrefix = "custom:";
    private static readonly HashSet<string> InboxLabelColors =
        ["violet", "amber", "orange", "emerald", "blue", "teal", "rose", "slate"];
    private static readonly WhatsAppInboxLabelDto[] FixedLabels =
    [
        new(SupplierLabel, "Proveedor", true),
        new(PendingLabel, "Pendiente", true),
        new(TakenLabel, "Tomado", false),
        new(PaymentLabel, "Pendiente de pago", true),
    ];

    private sealed class InboxLabelState
    {
        public required WhatsAppConversation Conversation { get; init; }
        public bool Pending { get; init; }
        public bool Taken { get; init; }
        public int? RemoteBranchId { get; init; }
        public bool WithoutOrder { get; init; }
    }

    private DateTime InboxDayStart() => ColombiaTimeHelper.GetTodayStartInUtcFromUtc(_clock.UtcNow);

    private IQueryable<WhatsAppConversation> ScopedInbox(IQueryable<WhatsAppConversation> query, int? branchId, bool unroutedOnly)
    {
        if (branchId.HasValue) query = query.Where(x => x.ChannelSettingId == null
            ? x.BranchId == branchId : x.OperationalBranchId == branchId);
        return unroutedOnly ? query.Where(x => x.ChannelSettingId != null && x.OperationalBranchId == null) : query;
    }

    private IQueryable<InboxLabelState> InboxLabelStates(IQueryable<WhatsAppConversation> query, DateTime start)
    {
        var end = start.AddDays(1);
        var orders = _db.Orders.AsNoTracking().Where(x => x.WhatsAppConversationId != null
            && x.Status != OrderStatus.Cancelled && x.Status != OrderStatus.AwaitingPayment);
        return from chat in query
               let branch = chat.ChannelSettingId == null ? chat.BranchId : chat.OperationalBranchId ?? 0
               let cutoff = _db.WhatsAppInboxTakenResets.Where(x => x.ScopeBranchId == branch)
                   .Select(x => (DateTime?)x.ClearedBefore).FirstOrDefault() ?? DateTime.UnixEpoch
               let latest = _db.WhatsAppCommerceSessions.Where(x => x.ConversationId == chat.Id)
                   .OrderByDescending(x => x.Id).Select(x => new { x.Status, x.CommercialStatus, x.CompletedAt, x.CreatedAt, x.UpdatedAt, x.BranchId }).FirstOrDefault()
               let inbound = chat.LastCustomerMessageAt ?? _db.WhatsAppMessages.Where(x =>
                   x.ConversationId == chat.Id && x.Direction == WhatsAppMessageDirection.Inbound && !x.IsHistorical)
                   .Max(x => (DateTime?)x.Timestamp)
               let pending = chat.PendingOrderMarkedAt != null && !orders.Any(x =>
                   x.WhatsAppConversationId == chat.Id && x.CreatedAt >= chat.PendingOrderMarkedAt)
               let destination = latest != null && latest.Status == "active" && latest.BranchId > 0
                   ? latest.BranchId
                   : orders.Where(x => x.WhatsAppConversationId == chat.Id).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                       .Select(x => (int?)x.BranchId).FirstOrDefault()
               let unresolvedMark = pending && (latest == null || latest.CommercialStatus != "no_sale" || chat.PendingOrderMarkedAt > latest.CompletedAt)
               select new InboxLabelState
               {
                   Conversation = chat,
                   Pending = unresolvedMark,
                   Taken = orders.Any(x => x.WhatsAppConversationId == chat.Id && x.CreatedAt >= cutoff && x.CreatedAt < end),
                   RemoteBranchId = branch > 0 && destination != branch ? destination : null,
                   WithoutOrder = (chat.Status == WhatsAppConversationStatus.Open || chat.Status == WhatsAppConversationStatus.Pending)
                   && chat.AttentionMode != WhatsAppAttentionMode.Closed && !chat.IsSupplier
                   && !orders.Any(x => x.WhatsAppConversationId == chat.Id && x.CreatedAt >= start && x.CreatedAt < end)
                   && ((inbound >= start && inbound < end && (latest == null || latest.CommercialStatus != "no_sale" || inbound > latest.CompletedAt))
                       || (latest != null && latest.Status == "active" && (latest.CreatedAt >= start || latest.UpdatedAt >= start))
                       || unresolvedMark)
               };
    }

    private async Task EnrichInboxLabelsAsync(IReadOnlyList<WhatsAppConversationDto> conversations, DateTime day, CancellationToken ct)
    {
        if (conversations.Count == 0) return;
        await WhatsAppOperationalContacts.EnrichAsync(_db, TenantId, conversations, ct);
        var ids = conversations.Select(x => x.Id).ToArray();
        var labels = await InboxLabelStates(_db.WhatsAppConversations.AsNoTracking().Where(x => ids.Contains(x.Id)), day)
            .Select(x => new
            {
                x.Conversation.Id, x.Conversation.IsSupplier, x.Pending, x.Taken, x.Conversation.IsPaymentPending, x.RemoteBranchId,
                CustomLabelIds = _db.WhatsAppConversationLabels.Where(y => y.ConversationId == x.Conversation.Id)
                    .OrderBy(y => y.LabelId).Select(y => y.LabelId).ToList()
            })
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var conversation in conversations)
        {
            if (!labels.TryGetValue(conversation.Id, out var entry)) continue;
            var values = new List<string>(5);
            if (entry.IsSupplier) values.Add(SupplierLabel);
            if (entry.Pending) values.Add(PendingLabel);
            if (entry.Taken) values.Add(TakenLabel);
            if (entry.IsPaymentPending) values.Add(PaymentLabel);
            if (entry.RemoteBranchId is int branchId) values.Add($"{BranchLabelPrefix}{branchId}");
            values.AddRange(entry.CustomLabelIds.Select(id => $"{CustomLabelPrefix}{id}"));
            conversation.Labels = values;
        }
    }

    [HttpGet("labels")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetInboxLabels(CancellationToken ct)
    {
        var branches = await _db.Branches.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var custom = await _db.WhatsAppInboxLabels.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<WhatsAppInboxLabelDto>>.SuccessResponse(
            FixedLabels.Concat(branches.Select(x => new WhatsAppInboxLabelDto($"{BranchLabelPrefix}{x.Id}", x.Name, false)))
                .Concat(custom.Select(ToInboxLabelDto)).ToArray()));
    }

    [HttpPost("labels")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public Task<IActionResult> CreateInboxLabel([FromBody] WhatsAppInboxLabelDefinitionDto request, CancellationToken ct) =>
        SaveInboxLabelAsync(null, request, ct);

    [HttpPut("labels/{key}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> UpdateInboxLabel(string key, [FromBody] WhatsAppInboxLabelDefinitionDto request, CancellationToken ct)
    {
        if (!TryGetCustomLabelId(key, out var id)) return NotFound();
        var label = await _db.WhatsAppInboxLabels.SingleOrDefaultAsync(x => x.Id == id, ct);
        return label is null ? NotFound() : await SaveInboxLabelAsync(label, request, ct);
    }

    [HttpDelete("labels/{key}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> DeleteInboxLabel(string key, CancellationToken ct)
    {
        if (!TryGetCustomLabelId(key, out var id)) return NotFound();
        var label = await _db.WhatsAppInboxLabels.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (label is null) return NotFound();
        _db.WhatsAppInboxLabels.Remove(label);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return NotFound(); }
        await PublishInboxChangeAsync(() => _whatsAppNotificationService.NotifyInboxLabelCatalogChangedAsync(ct));
        return NoContent();
    }

    private async Task<IActionResult> SaveInboxLabelAsync(WhatsAppInboxLabel? existing, WhatsAppInboxLabelDefinitionDto request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (name is null || name.Length is < 1 or > 40 || name.Any(char.IsControl))
            return BadRequest(ApiResponse<object>.ErrorResponse("Escribe un nombre de entre 1 y 40 caracteres."));
        name = name.Normalize();
        var normalized = name.ToUpperInvariant();
        if (name.Length > 40 || normalized.Length > 40)
            return BadRequest(ApiResponse<object>.ErrorResponse("Escribe un nombre de entre 1 y 40 caracteres."));
        if (request.Color is not { } color || !InboxLabelColors.Contains(color))
            return BadRequest(ApiResponse<object>.ErrorResponse("Elige un color de la paleta."));
        var id = existing?.Id ?? 0;
        if (FixedLabels.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            || await _db.WhatsAppInboxLabels.AnyAsync(x => x.NormalizedName == normalized && x.Id != id, ct))
            return DuplicateInboxLabelName();

        var label = existing ?? new WhatsAppInboxLabel { TenantId = TenantId };
        label.Name = name;
        label.NormalizedName = normalized;
        label.Color = color;
        if (existing is null) _db.WhatsAppInboxLabels.Add(label);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return NotFound(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_whatsapp_inbox_label_name" })
        {
            return DuplicateInboxLabelName();
        }

        await PublishInboxChangeAsync(() => _whatsAppNotificationService.NotifyInboxLabelCatalogChangedAsync(ct));
        return Ok(ApiResponse<WhatsAppInboxLabelDto>.SuccessResponse(ToInboxLabelDto(label)));
    }

    private IActionResult DuplicateInboxLabelName() =>
        Conflict(ApiResponse<object>.ErrorResponse("Ya existe una etiqueta con ese nombre."));

    private static WhatsAppInboxLabelDto ToInboxLabelDto(WhatsAppInboxLabel label) =>
        new($"{CustomLabelPrefix}{label.Id}", label.Name, true, label.Color, true);

    private static bool TryGetCustomLabelId(string key, out int id)
    {
        id = 0;
        return key.StartsWith(CustomLabelPrefix, StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(CustomLabelPrefix.Length), out id) && id > 0;
    }

    [HttpGet("inbox-summary")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> GetInboxSummary([FromQuery] int? branchId, [FromQuery] bool unroutedOnly, CancellationToken ct)
    {
        if (branchId.HasValue && !Roles.IsSuperadmin(_currentUser.Role) && branchId != _currentUser.BranchId) return Forbid();
        var scoped = ScopedInbox(GetAccessibleConversationsQuery().Where(x => x.Status != WhatsAppConversationStatus.Archived), branchId, unroutedOnly);
        var actorId = _currentUser.Id;
        var counts = await InboxLabelStates(scoped, InboxDayStart())
            .GroupBy(x => x.Conversation.AttentionMode == WhatsAppAttentionMode.Closed ? "closed"
                : x.Conversation.AssignedUserId == null ? "free"
                : x.Conversation.AssignedUserId == actorId ? "mine" : "team")
            .Select(x => new { x.Key, Pending = x.Count(y => y.WithoutOrder), Unread = x.Count(y => y.Conversation.UnreadCount > 0) })
            .ToDictionaryAsync(x => x.Key, x => new WhatsAppInboxTabCountsDto(x.Pending, x.Unread), ct);
        foreach (var key in new[] { "free", "mine", "team", "closed" }) counts.TryAdd(key, new(0, 0));
        return Ok(ApiResponse<Dictionary<string, WhatsAppInboxTabCountsDto>>.SuccessResponse(counts));
    }

    [HttpPut("conversations/{conversationId:int}/labels/{key}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> SetInboxLabel(int conversationId, string key, [FromBody] WhatsAppInboxLabelWriteDto request, CancellationToken ct)
    {
        var custom = TryGetCustomLabelId(key, out var labelId);
        if (!custom && key is not (SupplierLabel or PendingLabel or PaymentLabel))
            return BadRequest(ApiResponse<object>.ErrorResponse("La etiqueta es automática o no existe."));
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        await WhatsAppCommercialDraft.LockAsync(_db, TenantId, conversationId, ct);
        if (!await GetAccessibleConversationsQuery().AnyAsync(x => x.Id == conversationId, ct)) return NotFound();
        var chat = await _db.WhatsAppConversations.SingleAsync(x => x.Id == conversationId, ct);
        if (Roles.IsCashier(_currentUser.Role) && (chat.AttentionMode == WhatsAppAttentionMode.Closed
            || chat.AssignedUserId is int assigned && assigned != _currentUser.Id)) return Forbid();
        if (custom)
        {
            if (!await _db.WhatsAppInboxLabels.AnyAsync(x => x.Id == labelId, ct)) return NotFound();
            var assignment = await _db.WhatsAppConversationLabels.SingleOrDefaultAsync(x =>
                x.ConversationId == conversationId && x.LabelId == labelId, ct);
            if (request.Applied && assignment is null)
                _db.WhatsAppConversationLabels.Add(new() { TenantId = TenantId, ConversationId = conversationId, LabelId = labelId });
            if (!request.Applied && assignment is not null) _db.WhatsAppConversationLabels.Remove(assignment);
        }
        if (key == SupplierLabel) chat.IsSupplier = request.Applied;
        if (key == PaymentLabel) chat.IsPaymentPending = request.Applied;
        if (key == PendingLabel)
        {
            if (!request.Applied) chat.PendingOrderMarkedAt = null;
            else if (!await InboxLabelStates(_db.WhatsAppConversations.Where(x => x.Id == conversationId), InboxDayStart())
                .Select(x => x.Pending).SingleAsync(ct))
                chat.PendingOrderMarkedAt = _clock.UtcNow;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) when (custom) { return NotFound(); }
        catch (DbUpdateException error) when (custom && error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "fk_whatsapp_conversation_label_definition" })
        {
            return NotFound();
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await transaction.DisposeAsync();
        }
        var dto = ToConversationDto(chat);
        await EnrichInboxLabelsAsync([dto], InboxDayStart(), ct);
        await PublishInboxChangeAsync(() => _whatsAppNotificationService.NotifyInboxConversationChangedAsync(chat.Id, ct));
        return Ok(ApiResponse<IReadOnlyList<string>>.SuccessResponse(dto.Labels ?? []));
    }

    [HttpPost("labels/taken/reset")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<IActionResult> ResetInboxTaken([FromBody] WhatsAppInboxTakenResetDto request, CancellationToken ct)
    {
        var superadmin = Roles.IsSuperadmin(_currentUser.Role);
        if (request.UnroutedOnly && (!superadmin || request.BranchId.HasValue)) return Forbid();
        var branchId = request.BranchId ?? (superadmin ? null : _currentUser.BranchId);
        if (!request.UnroutedOnly && (!branchId.HasValue || branchId <= 0))
            return BadRequest(ApiResponse<object>.ErrorResponse("Selecciona una sucursal para limpiar «Tomado»."));
        if (branchId.HasValue && !superadmin && branchId != _currentUser.BranchId) return Forbid();
        if (branchId.HasValue && !await _db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive, ct)) return NotFound();
        var scopeBranch = branchId.GetValueOrDefault();
        var cutoff = InboxDayStart();
        if (_db.Database.IsRelational())
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO whatsapp_inbox_taken_reset (tenant_id, scope_branch_id, cleared_before)
                VALUES ({TenantId}, {scopeBranch}, {cutoff})
                ON CONFLICT (tenant_id, scope_branch_id) DO UPDATE SET cleared_before = EXCLUDED.cleared_before, updated_at = now()
                WHERE whatsapp_inbox_taken_reset.cleared_before < EXCLUDED.cleared_before
                """, ct);
        else
        {
            var reset = await _db.WhatsAppInboxTakenResets.SingleOrDefaultAsync(x => x.ScopeBranchId == scopeBranch, ct);
            if (reset is null) _db.WhatsAppInboxTakenResets.Add(new() { ScopeBranchId = scopeBranch, ClearedBefore = cutoff });
            else if (reset.ClearedBefore < cutoff) reset.ClearedBefore = cutoff;
            await _db.SaveChangesAsync(ct);
        }
        await PublishInboxChangeAsync(() => _whatsAppNotificationService.NotifyInboxScopeChangedAsync(scopeBranch, ct));
        return Ok(ApiResponse<object>.SuccessResponse(new { clearedBefore = cutoff }));
    }

    private async Task PublishInboxChangeAsync(Func<Task> publish)
    {
        try { await publish(); }
        catch (Exception error) { _logger.LogWarning(error, "WhatsApp inbox update could not be published."); }
    }
}
