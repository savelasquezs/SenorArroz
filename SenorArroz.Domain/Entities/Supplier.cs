using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Supplier : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Email { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<ExpenseHeader> ExpenseHeaders { get; set; } = new List<ExpenseHeader>();
    public virtual ICollection<SupplierExpense> SupplierExpenses { get; set; } = new List<SupplierExpense>();
}