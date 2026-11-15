using MediatR;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.OperationalChecklist;

public sealed record ReminderDefinitionDto(int Id, string Code, string Title, string Description,
    string Condition, string Period, int[] DaysOfWeek, bool ShiftMondayHoliday, string Responsible,
    string ManualPath, int SortOrder, bool IsActive, long Revision, long SyncedRevision);
public sealed record ChecklistItemDto(ReminderDefinitionDto Reminder, bool IsChecked, long StateRevision);
public sealed record ChecklistDto(int BranchId, DateOnly OperationDate, DateTime ServerTimeUtc,
    DateTime MorningAtUtc, DateTime EveningAtUtc, DateTime ResetAtUtc, List<ChecklistItemDto> Items);
public sealed record SaveReminderDto(int? Id, string Title, string Description, string Condition,
    string Period, int[] DaysOfWeek, bool ShiftMondayHoliday, string Responsible,
    string ManualPath, int SortOrder, bool IsActive, long ExpectedRevision);
public sealed record ChangeReminderStateDto(DateOnly OperationDate, bool IsChecked, long ExpectedRevision);
public sealed record ReminderSyncResult(string Status, string Message);
public sealed record SaveReminderResult(ReminderDefinitionDto Reminder, ReminderSyncResult Documentation);

public sealed record GetChecklistQuery : IRequest<ChecklistDto>;
public sealed record GetReminderConfigurationQuery(int Page = 1, int PageSize = 100) : IRequest<PagedResult<ReminderDefinitionDto>>;
public sealed record SaveReminderCommand(SaveReminderDto Data) : IRequest<SaveReminderResult>;
public sealed record ChangeReminderStateCommand(int Id, ChangeReminderStateDto Data) : IRequest<ChecklistItemDto>;
public sealed record SyncReminderDocumentationCommand : IRequest<ReminderSyncResult>;
public interface IReminderDocumentationSync
{
    Task<ReminderSyncResult> SyncAsync(CancellationToken ct);
}
public sealed class ReminderConflictException(string message) : Exception(message);
