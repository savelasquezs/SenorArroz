using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

internal static class InventoryEnumConversion
{
    public static string ToSnake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? $"_{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
    public static T FromSnake<T>(string value) where T : struct, Enum => Enum.Parse<T>(value.Replace("_", string.Empty), true);
}

public sealed class ExpenseUnitConversionConfiguration : IEntityTypeConfiguration<ExpenseUnitConversion>
{
    public void Configure(EntityTypeBuilder<ExpenseUnitConversion> b)
    {
        b.ToTable("expense_unit_conversion"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(x => x.BaseQuantity).HasColumnName("base_quantity").HasPrecision(18, 4);
        b.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Expense).WithMany(x => x.InventoryConversions).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ExpenseId, x.Name }).IsUnique();
    }
}

public sealed class ProductExpenseRequirementConfiguration : IEntityTypeConfiguration<ProductExpenseRequirement>
{
    public void Configure(EntityTypeBuilder<ProductExpenseRequirement> b)
    {
        b.ToTable("product_expense_requirement"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ProductId).HasColumnName("product_id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id");
        b.Property(x => x.BaseQuantity).HasColumnName("base_quantity").HasPrecision(18, 4);
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Product).WithMany(x => x.InventoryRequirements).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Expense).WithMany(x => x.ProductRequirements).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.ExpenseId }).IsUnique();
    }
}

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> b)
    {
        b.ToTable("inventory_balance"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BranchId).HasColumnName("branch_id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id");
        b.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand").HasPrecision(18, 4);
        b.Property(x => x.QuantityReserved).HasColumnName("quantity_reserved").HasPrecision(18, 4);
        b.Property(x => x.AverageUnitCost).HasColumnName("average_unit_cost").HasPrecision(18, 6);
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Expense).WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.ExpenseId }).IsUnique();
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.ToTable("inventory_movement"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BranchId).HasColumnName("branch_id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id");
        b.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).HasConversion(v => InventoryEnumConversion.ToSnake(v.ToString()), v => InventoryEnumConversion.FromSnake<InventoryMovementType>(v));
        b.Property(x => x.OnHandDelta).HasColumnName("on_hand_delta").HasPrecision(18, 4); b.Property(x => x.ReservedDelta).HasColumnName("reserved_delta").HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 6); b.Property(x => x.OrderId).HasColumnName("order_id");
        b.Property(x => x.ExpenseDetailId).HasColumnName("expense_detail_id"); b.Property(x => x.TransferId).HasColumnName("transfer_id");
        b.Property(x => x.InventoryCountId).HasColumnName("inventory_count_id"); b.Property(x => x.CreatedById).HasColumnName("created_by_id");
        b.Property(x => x.OperationKey).HasColumnName("operation_key").HasMaxLength(160).IsRequired(); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Expense).WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ExpenseDetail).WithMany().HasForeignKey(x => x.ExpenseDetailId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.OperationKey, x.ExpenseId, x.Type }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.CreatedAt });
    }
}

public sealed class OrderInventoryAllocationConfiguration : IEntityTypeConfiguration<OrderInventoryAllocation>
{
    public void Configure(EntityTypeBuilder<OrderInventoryAllocation> b)
    {
        b.ToTable("order_inventory_allocation"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OrderId).HasColumnName("order_id"); b.Property(x => x.ProductId).HasColumnName("product_id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id");
        b.Property(x => x.ControlMode).HasColumnName("control_mode").HasMaxLength(20).HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<InventoryControlMode>(v, true));
        b.Property(x => x.BaseQuantity).HasColumnName("base_quantity").HasPrecision(18, 4); b.Property(x => x.ReservedQuantity).HasColumnName("reserved_quantity").HasPrecision(18, 4); b.Property(x => x.ConsumedQuantity).HasColumnName("consumed_quantity").HasPrecision(18, 4);
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Order).WithMany(x => x.InventoryAllocations).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Expense).WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.OrderId, x.ProductId, x.ExpenseId }).IsUnique();
    }
}

public sealed class InventoryTransferConfiguration : IEntityTypeConfiguration<InventoryTransfer>
{
    public void Configure(EntityTypeBuilder<InventoryTransfer> b)
    {
        b.ToTable("inventory_transfer"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.SourceBranchId).HasColumnName("source_branch_id"); b.Property(x => x.DestinationBranchId).HasColumnName("destination_branch_id");
        b.Property(x => x.OperationKey).HasColumnName("operation_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).HasConversion(v => InventoryEnumConversion.ToSnake(v.ToString()), v => InventoryEnumConversion.FromSnake<InventoryTransferStatus>(v));
        b.Property(x => x.CreatedById).HasColumnName("created_by_id"); b.Property(x => x.DispatchedById).HasColumnName("dispatched_by_id"); b.Property(x => x.DispatchedAt).HasColumnName("dispatched_at"); b.Property(x => x.ReceivedById).HasColumnName("received_by_id"); b.Property(x => x.ReceivedAt).HasColumnName("received_at"); b.Property(x => x.DifferenceReason).HasColumnName("difference_reason").HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.SourceBranch).WithMany().HasForeignKey(x => x.SourceBranchId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.DestinationBranch).WithMany().HasForeignKey(x => x.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.OperationKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.DestinationBranchId, x.Status });
    }
}

public sealed class InventoryTransferLineConfiguration : IEntityTypeConfiguration<InventoryTransferLine>
{
    public void Configure(EntityTypeBuilder<InventoryTransferLine> b)
    {
        b.ToTable("inventory_transfer_line"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.TransferId).HasColumnName("transfer_id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id");
        b.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 4); b.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(18, 4); b.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 6); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Transfer).WithMany(x => x.Lines).HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Expense).WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.TransferId, x.ExpenseId }).IsUnique();
    }
}

public sealed class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> b)
    {
        b.ToTable("inventory_count"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.BranchId).HasColumnName("branch_id"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<InventoryCountStatus>(v, true)); b.Property(x => x.CreatedById).HasColumnName("created_by_id"); b.Property(x => x.ConfirmedById).HasColumnName("confirmed_by_id"); b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
    }
}

public sealed class InventoryCountLineConfiguration : IEntityTypeConfiguration<InventoryCountLine>
{
    public void Configure(EntityTypeBuilder<InventoryCountLine> b)
    {
        b.ToTable("inventory_count_line"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.InventoryCountId).HasColumnName("inventory_count_id"); b.Property(x => x.ExpenseId).HasColumnName("expense_id"); b.Property(x => x.ExpectedQuantity).HasColumnName("expected_quantity").HasPrecision(18, 4); b.Property(x => x.CountedQuantity).HasColumnName("counted_quantity").HasPrecision(18, 4); b.Property(x => x.Difference).HasColumnName("difference").HasPrecision(18, 4); b.Property(x => x.AverageUnitCost).HasColumnName("average_unit_cost").HasPrecision(18, 6); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne(x => x.InventoryCount).WithMany(x => x.Lines).HasForeignKey(x => x.InventoryCountId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Expense).WithMany().HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.TenantId, x.InventoryCountId, x.ExpenseId }).IsUnique();
    }
}
