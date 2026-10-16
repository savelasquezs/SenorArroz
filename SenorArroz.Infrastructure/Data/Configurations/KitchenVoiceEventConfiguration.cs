using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class KitchenVoiceEventConfiguration : IEntityTypeConfiguration<KitchenVoiceEvent>
{
    public void Configure(EntityTypeBuilder<KitchenVoiceEvent> builder)
    {
        builder.ToTable("kitchen_voice_event");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(24).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(e => new { e.TenantId, e.BranchId, e.Id })
            .HasDatabaseName("ix_kitchen_voice_event_tenant_branch_cursor");
        builder.HasIndex(e => new { e.TenantId, e.OrderId })
            .IsUnique().HasDatabaseName("ux_kitchen_voice_event_tenant_order");
        builder.HasOne<Order>().WithMany().HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
