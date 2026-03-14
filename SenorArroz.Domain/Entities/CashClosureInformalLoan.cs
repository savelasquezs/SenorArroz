using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Préstamos no oficiales / fiados en un cuadre de caja
/// Positivo = dinero que salió de caja (préstamo dado)
/// Negativo = dinero que nos deben (ej. "Deudas Maikol -300000")
/// </summary>
public class CashClosureInformalLoan : BaseEntity
{
    public int CashClosureId { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    // Navigation Properties
    public virtual CashRegisterClosure CashClosure { get; set; } = null!;
}
