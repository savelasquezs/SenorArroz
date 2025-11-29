using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class SupplierExpenseConfiguration : IEntityTypeConfiguration<SupplierExpense>
{
    public void Configure(EntityTypeBuilder<SupplierExpense> builder)
    {
        builder.ToTable("supplier_expense");

        builder.HasKey(se => se.Id);
        builder.Property(se => se.Id).HasColumnName("id");

        builder.Property(se => se.SupplierId).HasColumnName("supplier_id").IsRequired();
        builder.Property(se => se.ExpenseId).HasColumnName("expense_id").IsRequired();
        builder.Property(se => se.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
        builder.Property(se => se.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(se => se.LastUnitPrice).HasColumnName("last_unit_price").HasColumnType("decimal(18,2)");
        builder.Property(se => se.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(se => se.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(se => new { se.SupplierId, se.ExpenseId }).IsUnique();

        builder.HasOne(se => se.Supplier)
            .WithMany(s => s.SupplierExpenses)
            .HasForeignKey(se => se.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(se => se.Expense)
            .WithMany(e => e.SupplierExpenses)
            .HasForeignKey(se => se.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


