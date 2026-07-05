using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.ToTable("whatsapp_message");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.WhatsAppMessageId).HasColumnName("whatsapp_message_id").HasMaxLength(128);
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(20).IsRequired()
            .HasConversion(v => DirectionToDb(v), v => DirectionFromDb(v));
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(20).IsRequired()
            .HasConversion(v => TypeToDb(v), v => TypeFromDb(v));
        builder.Property(x => x.TextBody).HasColumnName("text_body").HasMaxLength(4096).IsRequired();
        builder.Property(x => x.MediaId).HasColumnName("media_id").HasMaxLength(128);
        builder.Property(x => x.MediaUrl).HasColumnName("media_url").HasMaxLength(2048);
        builder.Property(x => x.MediaMimeType).HasColumnName("media_mime_type").HasMaxLength(120);
        builder.Property(x => x.MediaFileName).HasColumnName("media_file_name").HasMaxLength(255);
        builder.Property(x => x.MediaFileSize).HasColumnName("media_file_size");
        builder.Property(x => x.MediaSha256).HasColumnName("media_sha256").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired()
            .HasConversion(v => StatusToDb(v), v => StatusFromDb(v));
        builder.Property(x => x.SentByUserId).HasColumnName("sent_by_user_id");
        builder.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
        builder.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SentByUser)
            .WithMany()
            .HasForeignKey(x => x.SentByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ConversationId).HasDatabaseName("idx_whatsapp_message_conversation");
        builder.HasIndex(x => x.WhatsAppMessageId).HasDatabaseName("idx_whatsapp_message_whatsapp_id");
        builder.HasIndex(x => x.MediaId).HasDatabaseName("idx_whatsapp_message_media_id");
        builder.HasIndex(x => x.Timestamp).HasDatabaseName("idx_whatsapp_message_timestamp");
    }

    private static string DirectionToDb(WhatsAppMessageDirection direction) => direction switch
    {
        WhatsAppMessageDirection.Inbound => "inbound",
        WhatsAppMessageDirection.Outbound => "outbound",
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static WhatsAppMessageDirection DirectionFromDb(string value) => value?.ToLowerInvariant() switch
    {
        "inbound" => WhatsAppMessageDirection.Inbound,
        "outbound" => WhatsAppMessageDirection.Outbound,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string TypeToDb(WhatsAppMessageType type) => type switch
    {
        WhatsAppMessageType.Text => "text",
        WhatsAppMessageType.Image => "image",
        WhatsAppMessageType.Audio => "audio",
        WhatsAppMessageType.Video => "video",
        WhatsAppMessageType.Document => "document",
        WhatsAppMessageType.Sticker => "sticker",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static WhatsAppMessageType TypeFromDb(string value) => value?.ToLowerInvariant() switch
    {
        "text" => WhatsAppMessageType.Text,
        "image" => WhatsAppMessageType.Image,
        "audio" => WhatsAppMessageType.Audio,
        "video" => WhatsAppMessageType.Video,
        "document" => WhatsAppMessageType.Document,
        "sticker" => WhatsAppMessageType.Sticker,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string StatusToDb(WhatsAppMessageStatus status) => status switch
    {
        WhatsAppMessageStatus.Received => "received",
        WhatsAppMessageStatus.Sent => "sent",
        WhatsAppMessageStatus.Delivered => "delivered",
        WhatsAppMessageStatus.Read => "read",
        WhatsAppMessageStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static WhatsAppMessageStatus StatusFromDb(string value) => value?.ToLowerInvariant() switch
    {
        "received" => WhatsAppMessageStatus.Received,
        "sent" => WhatsAppMessageStatus.Sent,
        "delivered" => WhatsAppMessageStatus.Delivered,
        "read" => WhatsAppMessageStatus.Read,
        "failed" => WhatsAppMessageStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
