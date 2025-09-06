using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class LoyaltyRuleConfiguration : IEntityTypeConfiguration<LoyaltyRule>
{
    public void Configure(EntityTypeBuilder<LoyaltyRule> builder)
    {
        builder.ToTable("loyalty_rule");

        builder.HasKey(lr => lr.Id);
        builder.Property(lr => lr.Id).HasColumnName("id");

        builder.Property(lr => lr.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(lr => lr.Description).HasColumnName("description").IsRequired();
        builder.Property(lr => lr.OrdersNeeded).HasColumnName("n_orders_needed").IsRequired();

        builder.Property(lr => lr.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(lr => lr.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(lr => lr.Branch)
            .WithMany(b => b.LoyaltyRules)
            .HasForeignKey(lr => lr.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}