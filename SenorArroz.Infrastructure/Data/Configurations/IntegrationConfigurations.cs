using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryAppConnectionConfiguration : IEntityTypeConfiguration<DeliveryAppConnection>
{
    public void Configure(EntityTypeBuilder<DeliveryAppConnection> b)
    {
        b.ToTable("delivery_app_connection");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BranchId).HasColumnName("branch_id");
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40);
        b.Property(x => x.Environment).HasColumnName("environment").HasMaxLength(20);
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        b.Property(x => x.ClientId).HasColumnName("client_id").HasMaxLength(255);
        b.Property(x => x.EncryptedClientSecret).HasColumnName("encrypted_client_secret").HasColumnType("text");
        b.Property(x => x.ExternalStoreId).HasColumnName("external_store_id").HasMaxLength(120);
        b.Property(x => x.FinancialAppId).HasColumnName("financial_app_id");
        b.Property(x => x.DefaultCookingTimeMinutes).HasColumnName("default_cooking_time_minutes");
        b.Property(x => x.EncryptedWebhookSecret).HasColumnName("encrypted_webhook_secret").HasColumnType("text");
        b.Property(x => x.WebhookConfigured).HasColumnName("webhook_configured");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.IsVerified).HasColumnName("is_verified");
        b.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
        b.Property(x => x.LastCatalogSyncAt).HasColumnName("last_catalog_sync_at");
        b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        Timestamps(b);
        b.HasIndex(x => new { x.BranchId, x.Provider }).IsUnique().HasDatabaseName("ux_delivery_app_connection_branch_provider");
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.FinancialApp).WithMany().HasForeignKey(x => x.FinancialAppId).OnDelete(DeleteBehavior.Restrict);
    }

    internal static void Timestamps<T>(EntityTypeBuilder<T> b) where T : Domain.Entities.Common.BaseEntity
    {
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}

public class DeliveryAppProductMappingConfiguration : IEntityTypeConfiguration<DeliveryAppProductMapping>
{
    public void Configure(EntityTypeBuilder<DeliveryAppProductMapping> b)
    {
        b.ToTable("delivery_app_product_mapping"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.ExternalProductId).HasColumnName("external_product_id").HasMaxLength(160);
        b.Property(x => x.ExternalSku).HasColumnName("external_sku").HasMaxLength(160); b.Property(x => x.ExternalName).HasColumnName("external_name").HasMaxLength(300);
        b.Property(x => x.ItemType).HasColumnName("item_type").HasMaxLength(30); b.Property(x => x.IsActive).HasColumnName("is_active"); b.Property(x => x.ProductId).HasColumnName("product_id");
        DeliveryAppConnectionConfiguration.Timestamps(b);
        b.HasIndex(x => new { x.ConnectionId, x.ExternalProductId, x.ItemType }).IsUnique().HasDatabaseName("ux_delivery_app_mapping_external");
        b.HasOne(x => x.Connection).WithMany(x => x.ProductMappings).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ExternalDeliveryOrderConfiguration : IEntityTypeConfiguration<ExternalDeliveryOrder>
{
    public void Configure(EntityTypeBuilder<ExternalDeliveryOrder> b)
    {
        b.ToTable("external_delivery_order"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.BranchId).HasColumnName("branch_id"); b.Property(x => x.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(160);
        b.Property(x => x.ExternalStoreId).HasColumnName("external_store_id").HasMaxLength(120); b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(200); b.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(50);
        b.Property(x => x.DeliveryAddress).HasColumnName("delivery_address").HasMaxLength(600); b.Property(x => x.DeliveryMethod).HasColumnName("delivery_method").HasMaxLength(40);
        b.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(60); b.Property(x => x.Total).HasColumnName("total"); b.Property(x => x.CookingTimeMinutes).HasColumnName("cooking_time_minutes");
        b.Property(x => x.RawPayloadJson).HasColumnName("raw_payload_json").HasColumnType("jsonb"); b.Property(x => x.LinesJson).HasColumnName("lines_json").HasColumnType("jsonb");
        b.Property(x => x.InternalOrderId).HasColumnName("internal_order_id"); b.Property(x => x.AcceptedByUserId).HasColumnName("accepted_by_user_id"); b.Property(x => x.AcceptedAt).HasColumnName("accepted_at"); b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        DeliveryAppConnectionConfiguration.Timestamps(b);
        b.HasIndex(x => new { x.ConnectionId, x.ExternalOrderId }).IsUnique().HasDatabaseName("ux_external_delivery_order_provider_id");
        b.HasOne(x => x.Connection).WithMany(x => x.ExternalOrders).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.InternalOrder).WithMany().HasForeignKey(x => x.InternalOrderId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class IntegrationWebhookEventConfiguration : IEntityTypeConfiguration<IntegrationWebhookEvent>
{
    public void Configure(EntityTypeBuilder<IntegrationWebhookEvent> b)
    {
        b.ToTable("integration_webhook_event"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40); b.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(200);
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80); b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40);
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000); b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        DeliveryAppConnectionConfiguration.Timestamps(b); b.HasIndex(x => new { x.ConnectionId, x.EventKey }).IsUnique().HasDatabaseName("ux_integration_webhook_event_key");
    }
}
