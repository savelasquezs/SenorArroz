using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class Expense : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public ExpenseUnit Unit { get; set; } = ExpenseUnit.Unit;

    // Navigation Properties
    public virtual ExpenseCategory Category { get; set; } = null!;
    public virtual ICollection<ExpenseDetail> ExpenseDetails { get; set; } = new List<ExpenseDetail>();
}