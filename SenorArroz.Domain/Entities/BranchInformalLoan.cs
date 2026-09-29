using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Préstamo informal vigente por sucursal (única fuente de verdad; no se duplica en cada cierre).
/// Positivo = dinero que salió de caja; negativo = ajuste tipo deuda a favor de caja.
/// </summary>
public class BranchInformalLoan : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public int? BankId { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int CreatedById { get; set; }

    public DateTime? DeactivatedAt { get; set; }
    public int? DeactivatedById { get; set; }
    /// <summary>Nota opcional al dar de baja lógica.</summary>
    public string? DeactivationNotes { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Bank? Bank { get; set; }
    public virtual User CreatedBy { get; set; } = null!;
    public virtual User? DeactivatedBy { get; set; }

    public virtual ICollection<BranchInformalLoanExemptOrder> ExemptOrders { get; set; } = new List<BranchInformalLoanExemptOrder>();
    public virtual ICollection<BranchInformalLoanPayment> Payments { get; set; } = new List<BranchInformalLoanPayment>();
}

public enum BranchInformalLoanPaymentKind
{
    Cash = 0,
    Expense = 1
}

public class BranchInformalLoanPayment : TenantOwnedEntity
{
    public int LoanId { get; set; }
    public BranchInformalLoanPaymentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Notes { get; set; }
    public int CreatedById { get; set; }
    public int? ExpenseHeaderId { get; set; }

    public virtual BranchInformalLoan Loan { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
    public virtual ExpenseHeader? ExpenseHeader { get; set; }
}
