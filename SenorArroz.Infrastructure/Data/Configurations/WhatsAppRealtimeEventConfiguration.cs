using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class WhatsAppRealtimeEventConfiguration : IEntityTypeConfiguration<WhatsAppRealtimeEvent>
{
    public void Configure(EntityTypeBuilder<WhatsAppRealtimeEvent> builder)
    {
        builder.ToTable("whatsapp_realtime_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(80);
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(x => x.Groups).HasColumnName("groups").HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(x => x.DeliveredTo).HasColumnName("delivered_to").HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_whatsapp_realtime_event_created");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_whatsapp_realtime_event_tenant");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
