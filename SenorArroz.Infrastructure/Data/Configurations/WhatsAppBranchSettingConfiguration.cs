using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppBranchSettingConfiguration : IEntityTypeConfiguration<WhatsAppBranchSetting>
{
    public void Configure(EntityTypeBuilder<WhatsAppBranchSetting> builder)
    {
        builder.ToTable("whatsapp_branch_setting");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.PhoneNumberId).HasColumnName("phone_number_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.BusinessAccountId).HasColumnName("business_account_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayPhoneNumber).HasColumnName("display_phone_number").HasMaxLength(32).IsRequired();
        builder.Property(x => x.AccessToken).HasColumnName("access_token").IsRequired();
        builder.Property(x => x.WebhookVerifyToken).HasColumnName("webhook_verify_token").HasMaxLength(255).IsRequired();
        builder.Property(x => x.AppSecret).HasColumnName("app_secret").HasMaxLength(255);
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(x => x.IsVerified).HasColumnName("is_verified").HasDefaultValue(false);
        builder.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
        builder.Property(x => x.AwayMessageEnabled).HasColumnName("away_message_enabled").HasDefaultValue(false);
        builder.Property(x => x.AwayMessageText).HasColumnName("away_message_text").HasMaxLength(3500);
        builder.Property(x => x.CoexistenceEnabled).HasColumnName("coexistence_enabled").HasDefaultValue(false);
        builder.Property(x => x.ConnectionState).HasColumnName("connection_state").HasMaxLength(32).HasDefaultValue("pending");
        builder.Property(x => x.ConnectionError).HasColumnName("connection_error").HasMaxLength(500);
        builder.Property(x => x.CoexistenceConnectedAt).HasColumnName("coexistence_connected_at");
        builder.Property(x => x.HistorySyncStatus).HasColumnName("history_sync_status").HasMaxLength(32).HasDefaultValue("not_started");
        builder.Property(x => x.ContactsSyncStatus).HasColumnName("contacts_sync_status").HasMaxLength(32).HasDefaultValue("not_started");
        builder.Property(x => x.HistorySyncRequestId).HasColumnName("history_sync_request_id").HasMaxLength(128);
        builder.Property(x => x.ContactsSyncRequestId).HasColumnName("contacts_sync_request_id").HasMaxLength(128);
        builder.Property(x => x.LastSyncAt).HasColumnName("last_sync_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithMany(b => b.WhatsAppSettings)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.BranchId, x.CoexistenceEnabled }).IsUnique().HasDatabaseName("ux_whatsapp_branch_setting_kind");
        builder.HasIndex(x => x.PhoneNumberId).IsUnique().HasFilter("phone_number_id <> ''").HasDatabaseName("ux_whatsapp_branch_setting_line");
        builder.HasIndex(x => x.WebhookVerifyToken).HasDatabaseName("idx_whatsapp_branch_setting_verify_token");
    }
}
