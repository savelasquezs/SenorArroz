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
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithOne(b => b.WhatsAppSetting)
            .HasForeignKey<WhatsAppBranchSetting>(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BranchId).IsUnique().HasDatabaseName("idx_whatsapp_branch_setting_branch");
        builder.HasIndex(x => x.PhoneNumberId).HasDatabaseName("idx_whatsapp_branch_setting_phone_number_id");
        builder.HasIndex(x => x.WebhookVerifyToken).HasDatabaseName("idx_whatsapp_branch_setting_verify_token");
    }
}
