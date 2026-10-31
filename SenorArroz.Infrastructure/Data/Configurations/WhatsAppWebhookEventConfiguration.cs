using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppWebhookEventConfiguration : IEntityTypeConfiguration<WhatsAppWebhookEvent>
{
    public void Configure(EntityTypeBuilder<WhatsAppWebhookEvent> builder)
    {
        builder.ToTable("whatsapp_webhook_event");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80).IsRequired();
        builder.Property(x => x.WhatsAppMessageId).HasColumnName("whatsapp_message_id").HasMaxLength(128);
        builder.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Processed).HasColumnName("processed").HasDefaultValue(false);
        builder.Property(x => x.ProcessingState).HasColumnName("processing_state").HasMaxLength(20).HasDefaultValue("legacy");
        builder.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(64);
        builder.Property(x => x.PartitionKey).HasColumnName("partition_key").HasMaxLength(64);
        builder.Property(x => x.PartitionAliases).HasColumnName("partition_aliases").HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(x => x.ProtectedPayload).HasColumnName("protected_payload");
        builder.Property(x => x.ProtectedResult).HasColumnName("protected_result");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.AttentionVersion).HasColumnName("attention_version");
        builder.Property(x => x.ProcessingAttempts).HasColumnName("processing_attempts").HasDefaultValue(0);
        builder.Property(x => x.LeaseToken).HasColumnName("lease_token");
        builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.ProcessingError).HasColumnName("processing_error").HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.DeduplicationKey }).IsUnique()
            .HasFilter("deduplication_key IS NOT NULL").HasDatabaseName("ux_whatsapp_webhook_deduplication");
        builder.HasIndex(x => new { x.ProcessingState, x.NextAttemptAt, x.Id }).HasDatabaseName("ix_whatsapp_webhook_queue");
        builder.HasIndex(x => new { x.TenantId, x.PartitionKey, x.Id }).HasDatabaseName("ix_whatsapp_webhook_partition");
        builder.HasIndex(x => x.PartitionAliases).HasMethod("gin").HasDatabaseName("ix_whatsapp_webhook_aliases");
        builder.HasIndex(x => new { x.TenantId, x.ConversationId, x.Id }).HasFilter("conversation_id IS NOT NULL")
            .HasDatabaseName("ix_whatsapp_webhook_conversation_queue");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_whatsapp_webhook_event_created_at");
        builder.HasIndex(x => x.WhatsAppMessageId).HasDatabaseName("idx_whatsapp_webhook_event_whatsapp_id");
    }
}
