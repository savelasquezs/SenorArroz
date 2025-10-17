using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.ToTable("bank_payment");

        builder.HasKey(bp => bp.Id);
        builder.Property(bp => bp.Id).HasColumnName("id");

        builder.Property(bp => bp.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(bp => bp.BankId).HasColumnName("bank_id").IsRequired();
        builder.Property(bp => bp.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(bp => bp.IsVerified).HasColumnName("is_verified").HasDefaultValue(false);
        builder.Property(bp => bp.VerifiedAt).HasColumnName("verified_at");

        builder.Property(bp => bp.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(bp => bp.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // Relaciones
        builder.HasOne(bp => bp.Order)
            .WithMany(o => o.BankPayments)
            .HasForeignKey(bp => bp.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(bp => bp.Bank)
            .WithMany(b => b.BankPayments)
            .HasForeignKey(bp => bp.BankId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}