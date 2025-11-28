using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseHeader : BaseEntity
{
    public int BranchId { get; set; }
    public int SupplierId { get; set; }
    public int CreatedById { get; set; }
    public int? Total { get; set; } // Nullable porque puede ser calculado por triggers

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual Supplier Supplier { get; set; } = null!;
    public virtual User CreatedBy { get; set; } = null!;
    public virtual ICollection<ExpenseDetail> ExpenseDetails { get; set; } = new List<ExpenseDetail>();
    public virtual ICollection<ExpenseBankPayment> ExpenseBankPayments { get; set; } = new List<ExpenseBankPayment>();
}