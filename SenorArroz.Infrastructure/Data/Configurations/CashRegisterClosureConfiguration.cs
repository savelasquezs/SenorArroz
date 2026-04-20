using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class CashRegisterClosureConfiguration : IEntityTypeConfiguration<CashRegisterClosure>
{
    public void Configure(EntityTypeBuilder<CashRegisterClosure> builder)
    {
        builder.ToTable("cash_register_closure");

        builder.HasKey(crc => crc.Id);
        builder.Property(crc => crc.Id).HasColumnName("id");

        builder.Property(crc => crc.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(crc => crc.ClosedAt).HasColumnName("closed_at").IsRequired();
        builder.Property(crc => crc.CreatedById).HasColumnName("created_by_id").IsRequired();
        builder.Property(crc => crc.OpeningCash).HasColumnName("opening_cash").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(crc => crc.ClosingCash).HasColumnName("closing_cash").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(crc => crc.DenominationCounts).HasColumnName("denomination_counts").HasMaxLength(500).HasDefaultValue("{}");
        builder.Property(crc => crc.PendingAppPaymentsSnapshot).HasColumnName("pending_app_payments_snapshot").HasMaxLength(8000).HasDefaultValue("[]");

        builder.Property(crc => crc.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(crc => crc.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(crc => crc.Branch)
            .WithMany(b => b.CashRegisterClosures)
            .HasForeignKey(crc => crc.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(crc => crc.CreatedBy)
            .WithMany()
            .HasForeignKey(crc => crc.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(crc => crc.BranchId);
        builder.HasIndex(crc => crc.ClosedAt);
    }
}
