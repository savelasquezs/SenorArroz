using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class AppPaymentConfiguration : IEntityTypeConfiguration<AppPayment>
{
    public void Configure(EntityTypeBuilder<AppPayment> builder)
    {
        builder.ToTable("app_payment");

        builder.HasKey(ap => ap.Id);
        builder.Property(ap => ap.Id).HasColumnName("id");

        builder.Property(ap => ap.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(ap => ap.AppId).HasColumnName("app_id").IsRequired();
        builder.Property(ap => ap.Amount).HasColumnName("amount").IsRequired();
        builder.Property(ap => ap.EstimatedCommissionRate).HasColumnName("estimated_commission_rate").HasPrecision(8, 6);
        builder.Property(ap => ap.EstimatedCommissionAmount).HasColumnName("estimated_commission_amount").HasPrecision(12, 2);
        builder.Property(ap => ap.ExpectedNetAmount).HasColumnName("expected_net_amount").HasPrecision(12, 2);
        builder.Property(ap => ap.ActualSettledAmount).HasColumnName("actual_settled_amount").HasPrecision(12, 2);
        builder.Property(ap => ap.SettlementVariance).HasColumnName("settlement_variance").HasPrecision(12, 2);
        builder.Property(ap => ap.IsSetted).HasColumnName("is_setted").HasDefaultValue(false);
        builder.Property(ap => ap.IsReversed).HasColumnName("is_reversed").HasDefaultValue(false);
        builder.Property(ap => ap.ReversedAt).HasColumnName("reversed_at");
        builder.Property(ap => ap.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(300);

        builder.Property(ap => ap.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(ap => ap.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // Relaciones
        builder.HasOne(ap => ap.Order)
            .WithMany(o => o.AppPayments)
            .HasForeignKey(ap => ap.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ap => ap.App)
            .WithMany(a => a.AppPayments)
            .HasForeignKey(ap => ap.AppId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
