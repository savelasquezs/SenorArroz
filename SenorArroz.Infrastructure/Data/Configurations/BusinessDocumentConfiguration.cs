using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class BusinessDocumentConfiguration : IEntityTypeConfiguration<BusinessDocument>
{
    public void Configure(EntityTypeBuilder<BusinessDocument> builder)
    {
        builder.ToTable("business_document");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.PublicId).HasColumnName("public_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DownloadUrl).HasColumnName("download_url").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.StorageObjectName).HasColumnName("storage_object_name").HasMaxLength(512).IsRequired();
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("ux_business_document_public_id");
        builder.HasIndex(x => x.Name).HasDatabaseName("ix_business_document_name");
        builder.HasIndex(x => x.UpdatedAt).HasDatabaseName("ix_business_document_updated_at");
    }
}
