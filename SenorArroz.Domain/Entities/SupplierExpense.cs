using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class SupplierExpense : BaseEntity
{
    public int SupplierId { get; set; }
    public int ExpenseId { get; set; }
    public int UsageCount { get; set; } = 0;
    public DateTime? LastUsedAt { get; set; }
    public decimal? LastUnitPrice { get; set; }

    public virtual Supplier Supplier { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}


