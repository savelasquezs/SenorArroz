namespace SenorArroz.Domain.Entities;

public class EntityAuditLog
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public DateTime ChangedAt { get; set; }
    public int? ChangedByUserId { get; set; }
    public string? ChangedByNameSnapshot { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public string MoneyDeltaJson { get; set; } = "{}";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? MetadataJson { get; set; }
}
