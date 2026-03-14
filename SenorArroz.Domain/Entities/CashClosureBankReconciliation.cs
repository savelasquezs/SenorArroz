using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Conciliación por banco en un cuadre de caja
/// Difference = ExpectedBalance - ActualBalance - Sum(Adjustments) debe ser 0 para guardar
/// </summary>
public class CashClosureBankReconciliation : BaseEntity
{
    public int CashClosureId { get; set; }
    public int BankId { get; set; }
    public decimal ExpectedBalance { get; set; }
    public decimal ActualBalance { get; set; }
    /// <summary>
    /// JSON: [{"concept":"Reservas pagas","amount":135000},...]
    /// </summary>
    public string Adjustments { get; set; } = "[]";
    public decimal Difference { get; set; }

    // Navigation Properties
    public virtual CashRegisterClosure CashClosure { get; set; } = null!;
    public virtual Bank Bank { get; set; } = null!;
}
