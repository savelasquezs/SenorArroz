using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppQuickReplyConfiguration : IEntityTypeConfiguration<WhatsAppQuickReply>
{
    public void Configure(EntityTypeBuilder<WhatsAppQuickReply> builder)
    {
        builder.ToTable("whatsapp_quick_reply");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.Shortcut).HasColumnName("shortcut").HasMaxLength(40).IsRequired();
        builder.Property(x => x.MessageTemplate).HasColumnName("message_template").HasMaxLength(4096).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
        builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithMany(x => x.WhatsAppQuickReplies)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BranchId).HasDatabaseName("idx_whatsapp_quick_reply_branch");
        builder.HasIndex(x => new { x.BranchId, x.Shortcut }).IsUnique().HasDatabaseName("uq_whatsapp_quick_reply_branch_shortcut");
        builder.HasIndex(x => new { x.BranchId, x.IsActive, x.UsageCount }).HasDatabaseName("idx_whatsapp_quick_reply_usage");
    }
}
