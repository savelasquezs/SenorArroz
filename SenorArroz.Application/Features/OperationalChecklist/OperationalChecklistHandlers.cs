using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Shared.Models;
using System.Text.RegularExpressions;

namespace SenorArroz.Application.Features.OperationalChecklist;

public sealed class OperationalChecklistHandlers(IApplicationDbContext db, ICurrentUser actor,
    IBranchContext branch, IClock clock, IReminderDocumentationSync documentation,
    IOrderNotificationService notifications, ILogger<OperationalChecklistHandlers> logger)
    : IRequestHandler<GetChecklistQuery, ChecklistDto>,
      IRequestHandler<GetReminderConfigurationQuery, PagedResult<ReminderDefinitionDto>>,
      IRequestHandler<SaveReminderCommand, SaveReminderResult>,
      IRequestHandler<ChangeReminderStateCommand, ChecklistItemDto>,
      IRequestHandler<SyncReminderDocumentationCommand, ReminderSyncResult>
{
    private int Scope(bool administrator = false)
    {
        if (!actor.IsAuthenticated || actor.TenantId <= 0 || (administrator
            ? !Roles.IsAdminOrSuperadmin(actor.Role) : !Roles.IsSuperadminOrAdminOrCashier(actor.Role)))
            throw new UnauthorizedAccessException();
        return branch.RequireBranch();
    }

    public async Task<ChecklistDto> Handle(GetChecklistQuery request, CancellationToken ct)
    {
        var branchId = Scope();
        var now = clock.UtcNow;
        var day = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(now);
        var rows = await db.OperationalReminders.AsNoTracking().Include(x => x.State)
            .Where(x => x.BranchId == branchId && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        DateTime At(int hour, int minute = 0) => ColombiaTimeHelper.ConvertColombiaToUtc(day.ToDateTime(new TimeOnly(hour, minute)));
        return new(branchId, day, now, At(10, 30), At(18), At(0).AddDays(1),
            rows.Where(x => ReminderSchedule.Applies(x, day)).Select(x => Item(x, day)).ToList());
    }

    public async Task<PagedResult<ReminderDefinitionDto>> Handle(GetReminderConfigurationQuery request, CancellationToken ct)
    {
        var branchId = Scope(true);
        var page = Math.Max(1, request.Page); var size = Math.Clamp(request.PageSize, 1, 100);
        var query = db.OperationalReminders.AsNoTracking().Where(x => x.BranchId == branchId);
        var count = await query.CountAsync(ct);
        return new() { Items = (await query.OrderBy(x => x.Period).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct)).Select(Map).ToList(),
            TotalCount = count, Page = page, PageSize = size, TotalPages = (int)Math.Ceiling((double)count / size) };
    }

    public async Task<SaveReminderResult> Handle(SaveReminderCommand request, CancellationToken ct)
    {
        var branchId = Scope(true); var d = request.Data;
        Validate(d);
        OperationalReminder row;
        if (d.Id is int id)
        {
            row = await db.OperationalReminders.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == branchId, ct)
                ?? throw new NotFoundException("Recordatorio no encontrado en esta sucursal.");
            if (row.Revision != d.ExpectedRevision) throw new ReminderConflictException("La configuración cambió. Recárgala antes de guardar.");
            row.Revision++;
        }
        else
        {
            if (await db.OperationalReminders.CountAsync(x => x.BranchId == branchId, ct) >= 100)
                throw new BusinessException("La sucursal ya tiene 100 recordatorios. Reutiliza o desactiva los existentes.");
            row = new() { TenantId = actor.TenantId, BranchId = branchId, Code = "POS-" + Guid.NewGuid().ToString("N")[..12],
                CreatedAt = clock.UtcNow, State = new() { TenantId = actor.TenantId, BranchId = branchId } };
            db.OperationalReminders.Add(row);
        }
        row.Title = d.Title.Trim(); row.Description = d.Description.Trim(); row.Condition = d.Condition.Trim();
        row.Period = d.Period; row.DaysOfWeek = d.DaysOfWeek.Distinct().Order().ToArray();
        row.ShiftMondayHoliday = d.ShiftMondayHoliday; row.Responsible = d.Responsible;
        row.ManualPath = d.ManualPath.Trim(); row.SortOrder = d.SortOrder; row.IsActive = d.IsActive; row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await Notify(branchId);
        // Configuration saves publish descriptions. Checking a box never writes to GitHub.
        var sync = await documentation.SyncAsync(ct);
        return new(Map(row), sync);
    }

    public async Task<ChecklistItemDto> Handle(ChangeReminderStateCommand request, CancellationToken ct)
    {
        var branchId = Scope(); var day = ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(clock.UtcNow);
        if (request.Data.OperationDate != day) throw new ReminderConflictException("Cambió el día de operación. Actualiza el checklist.");
        var row = await db.OperationalReminders.Include(x => x.State)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.BranchId == branchId, ct)
            ?? throw new NotFoundException("Recordatorio no encontrado en esta sucursal.");
        if (!ReminderSchedule.Applies(row, day)) throw new ReminderConflictException("Este recordatorio ya no corresponde a hoy.");
        var state = row.State ?? throw new BusinessException("Falta inicializar el estado del recordatorio.");
        var checkedToday = state.OperationDate == day && state.IsChecked;
        // Explicit desired state, not toggle: retried requests cannot invert a saved check.
        if (checkedToday == request.Data.IsChecked) return Item(row, day);
        if (state.Revision != request.Data.ExpectedRevision) throw new ReminderConflictException("Otra persona actualizó esta tarea. Revisa su estado actual.");
        state.OperationDate = day; state.IsChecked = request.Data.IsChecked; state.Revision++;
        await db.SaveChangesAsync(ct);
        await Notify(branchId);
        return Item(row, day);
    }

    public Task<ReminderSyncResult> Handle(SyncReminderDocumentationCommand request, CancellationToken ct)
    { Scope(true); return documentation.SyncAsync(ct); }

    private async Task Notify(int branchId)
    {
        try { await notifications.NotifyOperationalChecklistChanged(branchId); }
        catch (Exception e) { logger.LogWarning(e, "Checklist saved, realtime notification unavailable for branch {BranchId}", branchId); }
    }
    public static ReminderDefinitionDto Map(OperationalReminder x) => new(x.Id, x.Code, x.Title, x.Description,
        x.Condition, x.Period, x.DaysOfWeek, x.ShiftMondayHoliday, x.Responsible, x.ManualPath,
        x.SortOrder, x.IsActive, x.Revision, x.SyncedRevision);
    private static ChecklistItemDto Item(OperationalReminder x, DateOnly day) => new(Map(x),
        x.State?.OperationDate == day && x.State.IsChecked, x.State?.Revision ?? 0);

    private static void Validate(SaveReminderDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Length > 160 || string.IsNullOrWhiteSpace(d.Description)
            || d.Description.Length > 2000 || d.Condition is null || d.Condition.Length > 500
            || d.Period is not ("morning" or "evening") || d.Responsible is not ("cashier" or "admin")
            || d.DaysOfWeek is not { Length: > 0 and <= 6 } || d.DaysOfWeek.Any(x => x is < 0 or > 6 or 3)
            || d.DaysOfWeek.Distinct().Count() != d.DaysOfWeek.Length || d.SortOrder is < 0 or > 1000
            || d.ExpectedRevision < 0 || d.Id is <= 0
            || d.ManualPath is null || d.ManualPath.Length > 250 || !Regex.IsMatch(d.ManualPath, "^/operacion/[a-z0-9/-]+(?:#[a-z0-9-]+)?$"))
            throw new BusinessException("Revisa el título, descripción, días de operación y enlace al manual.");
        if (d.ShiftMondayHoliday && (d.DaysOfWeek.Length != 1 || d.DaysOfWeek[0] != 1))
            throw new BusinessException("El traslado por festivo se utiliza únicamente para tareas del lunes.");
    }
}
