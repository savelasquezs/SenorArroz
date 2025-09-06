using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ExpenseCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // Navigation Properties
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}