using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DiscountCodeConfiguration : IEntityTypeConfiguration<DiscountCode>
{
    public void Configure(EntityTypeBuilder<DiscountCode> builder)
    {
        builder.ToTable("discount_code");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.GiftProductId).HasColumnName("gift_product_id");
        builder.Property(x => x.DiscountPercentage).HasColumnName("discount_percentage").HasPrecision(5, 2);
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("ends_at");
        builder.Property(x => x.MinimumOrderValue).HasColumnName("minimum_order_value");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithMany(b => b.DiscountCodes)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GiftProduct)
            .WithMany(p => p.GiftDiscountCodes)
            .HasForeignKey(x => x.GiftProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.BranchId).HasDatabaseName("idx_discount_code_branch");
        builder.HasIndex(x => x.IsActive).HasDatabaseName("idx_discount_code_active");
        builder.HasIndex(x => x.StartsAt).HasDatabaseName("idx_discount_code_starts_at");
        builder.HasIndex(x => x.EndsAt).HasDatabaseName("idx_discount_code_ends_at");
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique().HasDatabaseName("ux_discount_code_branch_code");
    }
}
