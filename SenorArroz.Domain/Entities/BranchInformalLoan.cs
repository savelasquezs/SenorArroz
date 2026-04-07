using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>
/// Préstamo informal vigente por sucursal (única fuente de verdad; no se duplica en cada cierre).
/// Positivo = dinero que salió de caja; negativo = ajuste tipo deuda a favor de caja.
/// </summary>
public class BranchInformalLoan : BaseEntity
{
    public int BranchId { get; set; }
    public string Concept { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int CreatedById { get; set; }

    public DateTime? DeactivatedAt { get; set; }
    public int? DeactivatedById { get; set; }
    /// <summary>Nota opcional al dar de baja lógica.</summary>
    public string? DeactivationNotes { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
    public virtual User? DeactivatedBy { get; set; }

    public virtual ICollection<BranchInformalLoanExemptOrder> ExemptOrders { get; set; } = new List<BranchInformalLoanExemptOrder>();
}
