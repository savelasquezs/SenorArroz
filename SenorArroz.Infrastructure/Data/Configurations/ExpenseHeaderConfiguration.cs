using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ExpenseHeaderConfiguration : IEntityTypeConfiguration<ExpenseHeader>
{
    public void Configure(EntityTypeBuilder<ExpenseHeader> builder)
    {
        builder.ToTable("expense_header");

        builder.HasKey(eh => eh.Id);
        builder.Property(eh => eh.Id).HasColumnName("id");

        builder.Property(eh => eh.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(eh => eh.SupplierId).HasColumnName("supplier_id").IsRequired();
        builder.Property(eh => eh.CreatedById).HasColumnName("created_by_id").IsRequired();
        builder.Property(eh => eh.Total).HasColumnName("total");

        builder.Property(eh => eh.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(eh => eh.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Relaciones
        builder.HasOne(eh => eh.Branch)
            .WithMany(b => b.ExpenseHeaders)
            .HasForeignKey(eh => eh.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(eh => eh.Supplier)
            .WithMany(s => s.ExpenseHeaders)
            .HasForeignKey(eh => eh.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(eh => eh.CreatedBy)
            .WithMany(u => u.CreatedExpenseHeaders)
            .HasForeignKey(eh => eh.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices
        builder.HasIndex(eh => eh.BranchId).HasDatabaseName("idx_expense_header_branch");
        builder.HasIndex(eh => eh.SupplierId).HasDatabaseName("idx_expense_header_supplier");
        builder.HasIndex(eh => eh.CreatedById).HasDatabaseName("idx_expense_header_created_by");
    }
}