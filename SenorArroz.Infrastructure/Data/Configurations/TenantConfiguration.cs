using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.PublicId).HasColumnName("public_id").HasDefaultValueSql("gen_random_uuid()").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<TenantStatus>(value, true))
            .HasDefaultValue(TenantStatus.Active);
        builder.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(500);
        builder.Property(x => x.AccessVersion).HasColumnName("access_version").HasDefaultValue(1L);
        builder.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(150);
        builder.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(254);
        builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(32);
        builder.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200);
        builder.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(64);
        builder.Property(x => x.BillingAddress).HasColumnName("billing_address").HasMaxLength(500);
        builder.Property(x => x.SuspendedAt).HasColumnName("suspended_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("ux_tenant_public_id");
        builder.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_tenant_slug");
    }
}
