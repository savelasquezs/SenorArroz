using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseDetail : BaseEntity
{
    public int HeaderId { get; set; }
    public int ExpenseId { get; set; }
    public int Quantity { get; set; } = 1;
    public int Amount { get; set; }
    public int? Total { get; set; } // Nullable porque puede ser calculado por trigger

    // Navigation Properties
    public virtual ExpenseHeader Header { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}