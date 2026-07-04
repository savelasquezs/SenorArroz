using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppConversationConfiguration : IEntityTypeConfiguration<WhatsAppConversation>
{
    public void Configure(EntityTypeBuilder<WhatsAppConversation> builder)
    {
        builder.ToTable("whatsapp_conversation");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(150);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired()
            .HasConversion(v => StatusToDb(v), v => StatusFromDb(v));
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        builder.Property(x => x.LastMessagePreview).HasColumnName("last_message_preview").HasMaxLength(500);
        builder.Property(x => x.UnreadCount).HasColumnName("unread_count").HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithMany(b => b.WhatsAppConversations)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.BranchId).HasDatabaseName("idx_whatsapp_conversation_branch");
        builder.HasIndex(x => new { x.BranchId, x.PhoneNumber }).IsUnique().HasDatabaseName("idx_whatsapp_conversation_branch_phone");
        builder.HasIndex(x => x.LastMessageAt).HasDatabaseName("idx_whatsapp_conversation_last_message_at");
    }

    private static string StatusToDb(WhatsAppConversationStatus status) => status switch
    {
        WhatsAppConversationStatus.Open => "open",
        WhatsAppConversationStatus.Pending => "pending",
        WhatsAppConversationStatus.Closed => "closed",
        WhatsAppConversationStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static WhatsAppConversationStatus StatusFromDb(string value) => value?.ToLowerInvariant() switch
    {
        "open" => WhatsAppConversationStatus.Open,
        "pending" => WhatsAppConversationStatus.Pending,
        "closed" => WhatsAppConversationStatus.Closed,
        "archived" => WhatsAppConversationStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
