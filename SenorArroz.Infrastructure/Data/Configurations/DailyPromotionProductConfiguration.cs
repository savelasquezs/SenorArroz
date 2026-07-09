using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DailyPromotionProductConfiguration : IEntityTypeConfiguration<DailyPromotionProduct>
{
    public void Configure(EntityTypeBuilder<DailyPromotionProduct> builder)
    {
        builder.ToTable("daily_promotion_product");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DailyPromotionId).HasColumnName("daily_promotion_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.DailyPromotion)
            .WithMany(p => p.DiscountProducts)
            .HasForeignKey(x => x.DailyPromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
            .WithMany(p => p.DailyPromotionProducts)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.DailyPromotionId).HasDatabaseName("idx_daily_promotion_product_promotion");
        builder.HasIndex(x => x.ProductId).HasDatabaseName("idx_daily_promotion_product_product");
        builder.HasIndex(x => new { x.DailyPromotionId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("ux_daily_promotion_product_unique");
    }
}
