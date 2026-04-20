namespace SenorArroz.Application.Features.CashRegister.DTOs;

public class CashClosureDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateTime ClosedAt { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public decimal OpeningCash { get; set; }
    public decimal ClosingCash { get; set; }
    public string DenominationCounts { get; set; } = "{}";
    /// <summary>JSON: apps pendientes por liquidar al cierre (base para el siguiente período).</summary>
    public string PendingAppPaymentsSnapshot { get; set; } = "[]";
    public List<CashClosureBankReconciliationDto> BankReconciliations { get; set; } = new();
    public List<CashClosureInformalLoanDto> InformalLoans { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class CashClosureBankReconciliationDto
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public decimal ExpectedBalance { get; set; }
    public decimal ActualBalance { get; set; }
    public string Adjustments { get; set; } = "[]";
    public decimal Difference { get; set; }
}

public class CashClosureInformalLoanDto
{
    public int Id { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
