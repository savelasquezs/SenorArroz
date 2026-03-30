using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class PrintJob
{
    public long Id { get; set; }
    public int BranchId { get; set; }
    public PrintJobKind Kind { get; set; }
    public PrintJobStatus Status { get; set; }

    /// <summary>JSON array de enteros, p.ej. [1,2].</summary>
    public string OrderIdsJson { get; set; } = "[]";

    /// <summary>Snapshot para el agente (batch v1: objeto con orders[]).</summary>
    public string PayloadJson { get; set; } = "{}";

    public short PayloadVersion { get; set; } = 1;

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
}
