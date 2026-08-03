using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseDetail : BaseEntity
{
    public int HeaderId { get; set; }
    public int ExpenseId { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public int Amount { get; set; }
    public decimal? Total { get; set; } // Total por línea (cantidad × valor unitario)
    /// <summary>Indica si esta línea hace parte de la base gravable del IVA del comprobante.</summary>
    public bool IncludeVat { get; set; }

    /// <summary>Notas de la línea (opcional).</summary>
    public string? Notes { get; set; }

    // Navigation Properties
    public virtual ExpenseHeader Header { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}
