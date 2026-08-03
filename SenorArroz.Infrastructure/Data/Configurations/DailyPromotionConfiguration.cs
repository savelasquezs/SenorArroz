using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DailyPromotionConfiguration : IEntityTypeConfiguration<DailyPromotion>
{
    public void Configure(EntityTypeBuilder<DailyPromotion> builder)
    {
        builder.ToTable("daily_promotion");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.GiftProductId).HasColumnName("gift_product_id");
        builder.Property(x => x.DiscountPercentage).HasColumnName("discount_percentage").HasPrecision(5, 2);
        builder.Property(x => x.DiscountScope).HasColumnName("discount_scope").HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.MinimumOrderValue).HasColumnName("minimum_order_value");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("ends_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithMany(b => b.DailyPromotions)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GiftProduct)
            .WithMany(p => p.GiftDailyPromotions)
            .HasForeignKey(x => x.GiftProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(u => u.CreatedDailyPromotions)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.BranchId).HasDatabaseName("idx_daily_promotion_branch");
        builder.HasIndex(x => x.CreatedByUserId).HasDatabaseName("idx_daily_promotion_created_by_user");
        builder.HasIndex(x => x.IsActive).HasDatabaseName("idx_daily_promotion_active");
        builder.HasIndex(x => x.StartsAt).HasDatabaseName("idx_daily_promotion_starts_at");
        builder.HasIndex(x => x.EndsAt).HasDatabaseName("idx_daily_promotion_ends_at");
        builder.HasIndex(x => new { x.BranchId, x.IsActive, x.StartsAt, x.EndsAt })
            .HasDatabaseName("idx_daily_promotion_active_lookup");
    }
}
