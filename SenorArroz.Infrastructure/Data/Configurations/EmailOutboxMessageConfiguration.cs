using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox_message");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ToEmailsJson).HasColumnName("to_emails_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasColumnType("text").IsRequired();
        builder.Property(x => x.IsHtml).HasColumnName("is_html").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5).IsRequired();
        builder.Property(x => x.LastAttemptedAt).HasColumnName("last_attempted_at");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4000);
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100);
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("ix_email_outbox_message_status_next_attempt");
        builder.HasIndex(x => new { x.RelatedEntityType, x.RelatedEntityId }).HasDatabaseName("ix_email_outbox_message_related");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_email_outbox_message_created_at");
    }
}
