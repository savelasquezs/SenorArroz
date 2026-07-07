using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BranchAiSettingConfiguration : IEntityTypeConfiguration<BranchAiSetting>
{
    public void Configure(EntityTypeBuilder<BranchAiSetting> builder)
    {
        builder.ToTable("branch_ai_setting");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(120).IsRequired();
        builder.Property(x => x.ApiKey).HasColumnName("api_key").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(x => x.Temperature).HasColumnName("temperature");
        builder.Property(x => x.MaxContextMessages).HasColumnName("max_context_messages").HasDefaultValue(20);
        builder.Property(x => x.LastTestedAt).HasColumnName("last_tested_at");
        builder.Property(x => x.IsVerified).HasColumnName("is_verified").HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithOne(b => b.AiSetting)
            .HasForeignKey<BranchAiSetting>(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BranchId).IsUnique().HasDatabaseName("idx_branch_ai_setting_branch");
        builder.HasIndex(x => x.Provider).HasDatabaseName("idx_branch_ai_setting_provider");
    }
}
