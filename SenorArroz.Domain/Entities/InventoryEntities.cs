using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class ExpenseUnitConversion : TenantOwnedEntity
{
    public int ExpenseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BaseQuantity { get; set; }
    public bool Active { get; set; } = true;
    public virtual Expense Expense { get; set; } = null!;
}

public class ProductExpenseRequirement : TenantOwnedEntity
{
    public int ProductId { get; set; }
    public int ExpenseId { get; set; }
    public decimal BaseQuantity { get; set; }
    public virtual Product Product { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}

public class InventoryBalance : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public int ExpenseId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal QuantityReserved { get; set; }
    public decimal AverageUnitCost { get; set; }
    public virtual Branch Branch { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}

public class InventoryMovement : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public int ExpenseId { get; set; }
    public InventoryMovementType Type { get; set; }
    public decimal OnHandDelta { get; set; }
    public decimal ReservedDelta { get; set; }
    public decimal UnitCost { get; set; }
    public int? OrderId { get; set; }
    public int? ExpenseHeaderId { get; set; }
    public int? ExpenseDetailId { get; set; }
    public int? TransferId { get; set; }
    public int? InventoryCountId { get; set; }
    public int CreatedById { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public virtual Branch Branch { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
    public virtual Order? Order { get; set; }
    public virtual ExpenseDetail? ExpenseDetail { get; set; }
    public virtual User CreatedBy { get; set; } = null!;
}

public class OrderInventoryAllocation : TenantOwnedEntity
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int ExpenseId { get; set; }
    public InventoryControlMode ControlMode { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public virtual Order Order { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}

public class InventoryTransfer : TenantOwnedEntity
{
    public int SourceBranchId { get; set; }
    public int DestinationBranchId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public InventoryTransferStatus Status { get; set; } = InventoryTransferStatus.Draft;
    public int CreatedById { get; set; }
    public int? DispatchedById { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? ReceivedById { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? DifferenceReason { get; set; }
    public virtual Branch SourceBranch { get; set; } = null!;
    public virtual Branch DestinationBranch { get; set; } = null!;
    public virtual ICollection<InventoryTransferLine> Lines { get; set; } = new List<InventoryTransferLine>();
}

public class InventoryTransferLine : TenantOwnedEntity
{
    public int TransferId { get; set; }
    public int ExpenseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? ReceivedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public virtual InventoryTransfer Transfer { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}

public class InventoryCount : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public InventoryCountStatus Status { get; set; } = InventoryCountStatus.Draft;
    public int CreatedById { get; set; }
    public int? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<InventoryCountLine> Lines { get; set; } = new List<InventoryCountLine>();
}

public class InventoryCountLine : TenantOwnedEntity
{
    public int InventoryCountId { get; set; }
    public int ExpenseId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? Difference { get; set; }
    public decimal AverageUnitCost { get; set; }
    public virtual InventoryCount InventoryCount { get; set; } = null!;
    public virtual Expense Expense { get; set; } = null!;
}
