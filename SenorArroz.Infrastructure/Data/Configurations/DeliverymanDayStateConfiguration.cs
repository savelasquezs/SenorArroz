using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliverymanDayStateConfiguration : IEntityTypeConfiguration<DeliverymanDayState>
{
    public void Configure(EntityTypeBuilder<DeliverymanDayState> builder)
    {
        builder.ToTable("deliveryman_day_state");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(e => e.Date).HasColumnName("date").HasColumnType("date").IsRequired();
        builder.Property(e => e.LiquidationMode).HasColumnName("liquidation_mode")
            .HasConversion<int>()
            .HasDefaultValue(DeliverymanDayLiquidationMode.None)
            .IsRequired();
        builder.Property(e => e.Blocked).HasColumnName("blocked").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.UnlockedAt).HasColumnName("unlocked_at");
        builder.Property(e => e.UnlockedById).HasColumnName("unlocked_by_id");
        builder.Property(e => e.LastLiquidationAtUtc).HasColumnName("last_liquidation_at_utc");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(e => e.Branch)
            .WithMany()
            .HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Deliveryman)
            .WithMany()
            .HasForeignKey(e => e.DeliverymanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.UnlockedBy)
            .WithMany()
            .HasForeignKey(e => e.UnlockedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.BranchId, e.DeliverymanId, e.Date })
            .IsUnique()
            .HasDatabaseName("uq_deliveryman_day_state_branch_dm_date");
    }
}
