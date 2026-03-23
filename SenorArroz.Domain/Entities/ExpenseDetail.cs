using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseDetail : BaseEntity
{
    public int HeaderId { get; set; }
    public int ExpenseId { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public int Amount { get; set; }
    public decimal? Total { get; set; } // Total por línea (cantidad × valor unitario)

    // Navigation Properties
    public virtual ExpenseHeader Header { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}