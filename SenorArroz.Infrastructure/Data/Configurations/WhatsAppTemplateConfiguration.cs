using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> builder)
    {
        builder.ToTable("whatsapp_template");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.BusinessAccountId).HasColumnName("business_account_id").HasMaxLength(64);
        builder.Property(x => x.MetaTemplateId).HasColumnName("meta_template_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.Language).HasColumnName("language").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Components).HasColumnName("components").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Branch)
            .WithMany(x => x.WhatsAppTemplates)
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.MetaTemplateId).IsUnique().HasDatabaseName("uq_whatsapp_template_meta_id");
        builder.HasIndex(x => new { x.BusinessAccountId, x.Name, x.Language }).HasDatabaseName("idx_whatsapp_template_account_name_language");
        builder.HasIndex(x => x.Status).HasDatabaseName("idx_whatsapp_template_status");
        builder.HasIndex(x => x.BranchId).HasDatabaseName("idx_whatsapp_template_branch");
    }
}
