using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Representa un cuadre de caja (cierre de caja diario)
/// </summary>
public class CashRegisterClosure : BaseEntity
{
    public int BranchId { get; set; }
    public DateTime ClosedAt { get; set; }
    public int CreatedById { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ClosingCash { get; set; }
    /// <summary>
    /// JSON: {"50000":6,"20000":18,"10000":28,...} - cantidad por denominación
    /// </summary>
    public string DenominationCounts { get; set; } = "{}";

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
    public virtual ICollection<CashClosureBankReconciliation> BankReconciliations { get; set; } = new List<CashClosureBankReconciliation>();
    public virtual ICollection<CashClosureInformalLoan> InformalLoans { get; set; } = new List<CashClosureInformalLoan>();
}
