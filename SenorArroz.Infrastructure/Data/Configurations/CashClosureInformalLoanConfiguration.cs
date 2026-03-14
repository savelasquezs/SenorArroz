using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class CashClosureInformalLoanConfiguration : IEntityTypeConfiguration<CashClosureInformalLoan>
{
    public void Configure(EntityTypeBuilder<CashClosureInformalLoan> builder)
    {
        builder.ToTable("cash_closure_informal_loan");

        builder.HasKey(ccil => ccil.Id);
        builder.Property(ccil => ccil.Id).HasColumnName("id");

        builder.Property(ccil => ccil.CashClosureId).HasColumnName("cash_closure_id").IsRequired();
        builder.Property(ccil => ccil.Concept).HasColumnName("concept").HasMaxLength(200).IsRequired();
        builder.Property(ccil => ccil.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(ccil => ccil.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(ccil => ccil.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(ccil => ccil.CashClosure)
            .WithMany(crc => crc.InformalLoans)
            .HasForeignKey(ccil => ccil.CashClosureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ccil => ccil.CashClosureId);
    }
}
