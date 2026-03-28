using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ExpenseMenuTargetConfiguration : IEntityTypeConfiguration<ExpenseMenuTarget>
{
    public void Configure(EntityTypeBuilder<ExpenseMenuTarget> builder)
    {
        builder.ToTable("expense_menu_target");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ExpenseId).HasColumnName("expense_id").IsRequired();
        builder.Property(e => e.TargetType).HasColumnName("target_type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.TargetId).HasColumnName("target_id").IsRequired();

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(e => new { e.ExpenseId, e.TargetType, e.TargetId }).IsUnique();

        builder.HasOne(e => e.Expense)
            .WithMany(x => x.MenuTargets)
            .HasForeignKey(e => e.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
