namespace SenorArroz.Domain.Entities;

public class DailyAuditDispatch
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public DateTime BusinessDate { get; set; }
    public int CashRegisterClosureId { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? DispatchedByUserId { get; set; }
    public string DispatchStatus { get; set; } = string.Empty;
    public string? DispatchError { get; set; }
    public string RecipientEmailsJson { get; set; } = "[]";
    public string SummaryJson { get; set; } = "{}";

    public virtual Branch Branch { get; set; } = null!;
    public virtual CashRegisterClosure CashRegisterClosure { get; set; } = null!;
    public virtual User? DispatchedByUser { get; set; }
}
