using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class CashClosureBankReconciliationConfiguration : IEntityTypeConfiguration<CashClosureBankReconciliation>
{
    public void Configure(EntityTypeBuilder<CashClosureBankReconciliation> builder)
    {
        builder.ToTable("cash_closure_bank_reconciliation");

        builder.HasKey(ccbr => ccbr.Id);
        builder.Property(ccbr => ccbr.Id).HasColumnName("id");

        builder.Property(ccbr => ccbr.CashClosureId).HasColumnName("cash_closure_id").IsRequired();
        builder.Property(ccbr => ccbr.BankId).HasColumnName("bank_id").IsRequired();
        builder.Property(ccbr => ccbr.ExpectedBalance).HasColumnName("expected_balance").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(ccbr => ccbr.ActualBalance).HasColumnName("actual_balance").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(ccbr => ccbr.Adjustments).HasColumnName("adjustments").HasMaxLength(2000);
        builder.Property(ccbr => ccbr.Difference).HasColumnName("difference").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(ccbr => ccbr.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(ccbr => ccbr.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(ccbr => ccbr.CashClosure)
            .WithMany(crc => crc.BankReconciliations)
            .HasForeignKey(ccbr => ccbr.CashClosureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ccbr => ccbr.Bank)
            .WithMany()
            .HasForeignKey(ccbr => ccbr.BankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ccbr => ccbr.CashClosureId);
        builder.HasIndex(ccbr => ccbr.BankId);
    }
}
