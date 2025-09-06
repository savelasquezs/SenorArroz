using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseBankPayment : BaseEntity
{
    public int BankId { get; set; }
    public int ExpenseHeaderId { get; set; }
    public decimal Amount { get; set; }

    // Navigation Properties
    public virtual Bank Bank { get; set; } = null!;
    public virtual ExpenseHeader ExpenseHeader { get; set; } = null!;
}
