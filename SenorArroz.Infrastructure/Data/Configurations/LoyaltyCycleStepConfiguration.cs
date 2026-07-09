using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class LoyaltyCycleStepConfiguration : IEntityTypeConfiguration<LoyaltyCycleStep>
{
    public void Configure(EntityTypeBuilder<LoyaltyCycleStep> builder)
    {
        builder.ToTable("loyalty_cycle_step");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.StepIndex).HasColumnName("step_index").IsRequired();
        builder.Property(x => x.RewardLabel).HasColumnName("reward_label").IsRequired();
        builder.Property(x => x.StepName).HasColumnName("step_name").HasMaxLength(200);
        builder.Property(x => x.RewardType).HasColumnName("reward_type").HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.GiftProductId).HasColumnName("gift_product_id");
        builder.Property(x => x.DiscountPercentage).HasColumnName("discount_percentage").HasPrecision(5, 2);
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => new { x.BranchId, x.StepIndex }).IsUnique().HasDatabaseName("UQ_loyalty_cycle_step_branch_step");
        builder.HasIndex(x => x.IsActive).HasDatabaseName("idx_loyalty_cycle_step_active");

        builder.HasOne(x => x.Branch)
            .WithMany(b => b.LoyaltyCycleSteps)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.GiftProduct)
            .WithMany(p => p.LoyaltyGiftSteps)
            .HasForeignKey(x => x.GiftProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
