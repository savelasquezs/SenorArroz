using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class Expense : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public ExpenseUnit Unit { get; set; } = ExpenseUnit.Unit;
    public bool TracksInventory { get; set; }
    public bool InventoryActive { get; set; }
    public InventoryBaseUnit InventoryBaseUnit { get; set; } = InventoryBaseUnit.Unit;

    // Navigation Properties
    public virtual ExpenseCategory Category { get; set; } = null!;
    public virtual ICollection<ExpenseDetail> ExpenseDetails { get; set; } = new List<ExpenseDetail>();
    public virtual ICollection<SupplierExpense> SupplierExpenses { get; set; } = new List<SupplierExpense>();
    public virtual ICollection<ExpenseMenuTarget> MenuTargets { get; set; } = new List<ExpenseMenuTarget>();
    public virtual ICollection<ExpenseUnitConversion> InventoryConversions { get; set; } = new List<ExpenseUnitConversion>();
    public virtual ICollection<ProductExpenseRequirement> ProductRequirements { get; set; } = new List<ProductExpenseRequirement>();
}
