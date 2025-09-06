using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ExpenseDetailConfiguration : IEntityTypeConfiguration<ExpenseDetail>
{
    public void Configure(EntityTypeBuilder<ExpenseDetail> builder)
    {
        builder.ToTable("expense_detail");

        builder.HasKey(ed => ed.Id);
        builder.Property(ed => ed.Id).HasColumnName("id");

        builder.Property(ed => ed.HeaderId).HasColumnName("header_id").IsRequired();
        builder.Property(ed => ed.ExpenseId).HasColumnName("expense_id").IsRequired();
        builder.Property(ed => ed.Quantity).HasColumnName("quantity").HasDefaultValue(1);
        builder.Property(ed => ed.Amount).HasColumnName("amount").IsRequired();
        builder.Property(ed => ed.Total).HasColumnName("total");

        builder.Property(ed => ed.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(ed => ed.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Relaciones
        builder.HasOne(ed => ed.Header)
            .WithMany(eh => eh.ExpenseDetails)
            .HasForeignKey(ed => ed.HeaderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ed => ed.Expense)
            .WithMany(e => e.ExpenseDetails)
            .HasForeignKey(ed => ed.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices
        builder.HasIndex(ed => ed.HeaderId).HasDatabaseName("idx_expense_detail_header");
    }
}