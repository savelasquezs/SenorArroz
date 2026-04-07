namespace SenorArroz.Domain.Entities;

/// <summary>
/// Pedido que queda excluido del bloqueo de cuadre (listo/en camino) mientras el préstamo informal siga activo.
/// </summary>
public class BranchInformalLoanExemptOrder
{
    public int LoanId { get; set; }
    public int OrderId { get; set; }

    public virtual BranchInformalLoan Loan { get; set; } = null!;
    public virtual Order Order { get; set; } = null!;
}
