namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CashClosureAuditSummaryDto
{
    public int CashClosureId { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string BusinessDate { get; set; } = string.Empty;
    public string DispatchStatus { get; set; } = "not_sent";
    public string? DispatchError { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public List<string> RecipientEmails { get; set; } = new();
    public List<CashClosureAuditGroupDto> Groups { get; set; } = new();
    public List<CashClosureAuditEventDto> Events { get; set; } = new();
}

public class CashClosureAuditGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public decimal NetDifference { get; set; }
    public List<string> Details { get; set; } = new();
}

public class CashClosureAuditEventDto
{
    public int Id { get; set; }
    public DateTime ChangedAt { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public decimal? TotalBefore { get; set; }
    public decimal? TotalAfter { get; set; }
    public decimal Difference { get; set; }
}
