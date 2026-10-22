using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Entities.Common;
using System.Text.RegularExpressions;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class ElectronicInvoiceIntegrationConfiguration : IEntityTypeConfiguration<ElectronicInvoiceIntegration>
{
    public void Configure(EntityTypeBuilder<ElectronicInvoiceIntegration> b)
    {
        Map(b, "electronic_invoice_integration");
        b.Property(x => x.CompanyJson).HasColumnType("jsonb");
        b.Property(x => x.ResolutionsJson).HasColumnType("jsonb");
        b.Property(x => x.NextNumber).IsConcurrencyToken();
        b.Property(x => x.NextCreditNumber).IsConcurrencyToken();
        b.HasIndex(x => x.TenantId).IsUnique().HasFilter("is_active");
        b.HasIndex(x => new { x.TenantId, x.Id }).IsUnique();
    }

    internal static void Map<T>(EntityTypeBuilder<T> b, string table) where T : TenantOwnedEntity
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        foreach (var property in typeof(T).GetProperties().Where(p => !p.GetMethod!.IsVirtual))
            b.Property(property.Name).HasColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
    }
}

public sealed class CustomerBillingRecipientConfiguration : IEntityTypeConfiguration<CustomerBillingRecipient>
{
    public void Configure(EntityTypeBuilder<CustomerBillingRecipient> b)
    {
        ElectronicInvoiceIntegrationConfiguration.Map(b, "customer_billing_recipient");
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.DocumentType, x.Identification }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique().HasFilter("is_default");
        b.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.TenantId, x.CustomerId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ElectronicInvoiceDocumentConfiguration : IEntityTypeConfiguration<ElectronicInvoiceDocument>
{
    public void Configure(EntityTypeBuilder<ElectronicInvoiceDocument> b)
    {
        ElectronicInvoiceIntegrationConfiguration.Map(b, "electronic_invoice_document");
        b.Property(x => x.RecipientJson).HasColumnType("jsonb");
        b.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        b.Property(x => x.PayloadJson).HasColumnType("jsonb");
        b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        b.Property(x => x.LeaseUntil).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique().HasFilter("kind = 'invoice' AND status NOT IN ('credited', 'cancelled')");
        b.HasIndex(x => x.OriginalDocumentId).IsUnique().HasFilter("original_document_id IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.IntegrationId, x.Environment, x.Kind, x.Prefix, x.Number }).IsUnique();
        b.HasOne<Order>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrderId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ElectronicInvoiceIntegration>().WithMany().HasForeignKey(x => new { x.TenantId, x.IntegrationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ElectronicInvoiceDocument>().WithMany().HasForeignKey(x => new { x.TenantId, x.OriginalDocumentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
