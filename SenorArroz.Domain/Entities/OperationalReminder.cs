using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class OperationalReminder : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Period { get; set; } = "morning";
    public int[] DaysOfWeek { get; set; } = [0, 1, 2, 4, 5, 6];
    public bool ShiftMondayHoliday { get; set; }
    public string Responsible { get; set; } = "cashier";
    public string ManualPath { get; set; } = "/operacion/responsable-caja";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public long Revision { get; set; } = 1;
    public long SyncedRevision { get; set; }
    public OperationalReminderState State { get; set; } = null!;
}

// One mutable row per reminder. No employee tracking or compliance history.
public sealed class OperationalReminderState : ITenantOwned
{
    public int TenantId { get; set; }
    public int BranchId { get; set; }
    public int ReminderId { get; set; }
    public DateOnly? OperationDate { get; set; }
    public bool IsChecked { get; set; }
    public long Revision { get; set; }
}
