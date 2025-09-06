using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_category");

        builder.HasKey(ec => ec.Id);
        builder.Property(ec => ec.Id).HasColumnName("id");

        builder.Property(ec => ec.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        builder.Property(ec => ec.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(ec => ec.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}