using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryAppConnectionConfiguration : IEntityTypeConfiguration<DeliveryAppConnection>
{
    public void Configure(EntityTypeBuilder<DeliveryAppConnection> builder)
    {
        builder.ToTable("delivery_app_connection");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40);
        builder.Property(x => x.Environment).HasColumnName("environment").HasMaxLength(20);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        builder.Property(x => x.PublicId).HasColumnName("public_id");
        builder.Property(x => x.FinancialAppId).HasColumnName("financial_app_id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.TechnicalUserId).HasColumnName("technical_user_id");
        builder.Property(x => x.DefaultCookingTimeMinutes).HasColumnName("default_cooking_time_minutes");
        builder.Property(x => x.EstimatedCommissionRate).HasColumnName("estimated_commission_rate").HasPrecision(8, 6);
        builder.Property(x => x.PiiRetentionDays).HasColumnName("pii_retention_days");
        builder.Property(x => x.WebhookConfigured).HasColumnName("webhook_configured");
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.IsVerified).HasColumnName("is_verified");
        builder.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
        builder.Property(x => x.LastMenuPublishedAt).HasColumnName("last_menu_published_at");
        builder.Property(x => x.LastAvailabilitySyncAt).HasColumnName("last_availability_sync_at");
        builder.Property(x => x.LastWebhookAt).HasColumnName("last_webhook_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        Timestamps(builder);

        builder.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("ux_delivery_app_connection_public_id");
        builder.HasIndex(x => new { x.BranchId, x.Provider }).IsUnique().HasDatabaseName("ux_delivery_app_connection_branch_provider");
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinancialApp).WithMany().HasForeignKey(x => x.FinancialAppId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TechnicalUser).WithMany().HasForeignKey(x => x.TechnicalUserId).OnDelete(DeleteBehavior.Restrict);
    }

    internal static void Timestamps<T>(EntityTypeBuilder<T> builder) where T : Domain.Entities.Common.BaseEntity
    {
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}

public class DeliveryAppStoreConfiguration : IEntityTypeConfiguration<DeliveryAppStore>
{
    public void Configure(EntityTypeBuilder<DeliveryAppStore> builder)
    {
        builder.ToTable("delivery_app_store");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.RappiStoreId).HasColumnName("rappi_store_id").HasMaxLength(120);
        builder.Property(x => x.StoreIntegrationId).HasColumnName("store_integration_id").HasMaxLength(120);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
        builder.Property(x => x.IsParent).HasColumnName("is_parent");
        builder.Property(x => x.ManualReadyForPickupEnabled).HasColumnName("manual_ready_for_pickup_enabled");
        builder.Property(x => x.ConnectivityEnabled).HasColumnName("connectivity_enabled");
        builder.Property(x => x.LastPingAt).HasColumnName("last_ping_at");
        builder.Property(x => x.LastConnectivityAt).HasColumnName("last_connectivity_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.ConnectionId, x.RappiStoreId }).IsUnique().HasDatabaseName("ux_delivery_app_store_rappi");
        builder.HasOne(x => x.Connection).WithMany(x => x.Stores).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DeliveryAppWebhookSubscriptionConfiguration : IEntityTypeConfiguration<DeliveryAppWebhookSubscription>
{
    public void Configure(EntityTypeBuilder<DeliveryAppWebhookSubscription> builder)
    {
        builder.ToTable("delivery_app_webhook_subscription");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80);
        builder.Property(x => x.EncryptedSecret).HasColumnName("encrypted_secret").HasColumnType("text");
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.LastReceivedAt).HasColumnName("last_received_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.ConnectionId, x.EventType }).IsUnique().HasDatabaseName("ux_delivery_app_webhook_event");
        builder.HasOne(x => x.Connection).WithMany(x => x.WebhookSubscriptions).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DeliveryAppProductMappingConfiguration : IEntityTypeConfiguration<DeliveryAppProductMapping>
{
    public void Configure(EntityTypeBuilder<DeliveryAppProductMapping> builder)
    {
        builder.ToTable("delivery_app_product_mapping");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(160);
        builder.Property(x => x.CategorySku).HasColumnName("category_sku").HasMaxLength(160);
        builder.Property(x => x.IsSelected).HasColumnName("is_selected");
        builder.Property(x => x.OverrideName).HasColumnName("override_name").HasMaxLength(300);
        builder.Property(x => x.OverrideDescription).HasColumnName("override_description").HasMaxLength(1000);
        builder.Property(x => x.OverrideImageUrl).HasColumnName("override_image_url").HasMaxLength(1000);
        builder.Property(x => x.OverridePrice).HasColumnName("override_price");
        builder.Property(x => x.PublishedName).HasColumnName("published_name").HasMaxLength(300);
        builder.Property(x => x.PublishedDescription).HasColumnName("published_description").HasMaxLength(1000);
        builder.Property(x => x.PublishedImageUrl).HasColumnName("published_image_url").HasMaxLength(1000);
        builder.Property(x => x.PublishedPrice).HasColumnName("published_price");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.ConnectionId, x.ProductId }).IsUnique().HasDatabaseName("ux_delivery_app_mapping_product");
        builder.HasIndex(x => new { x.ConnectionId, x.Sku }).IsUnique().HasDatabaseName("ux_delivery_app_mapping_sku");
        builder.HasOne(x => x.Connection).WithMany(x => x.ProductMappings).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExternalDeliveryOrderConfiguration : IEntityTypeConfiguration<ExternalDeliveryOrder>
{
    public void Configure(EntityTypeBuilder<ExternalDeliveryOrder> builder)
    {
        builder.ToTable("external_delivery_order");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.StoreId).HasColumnName("store_id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(160);
        builder.Property(x => x.ExternalStoreId).HasColumnName("external_store_id").HasMaxLength(120);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(200);
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(50);
        builder.Property(x => x.DeliveryAddress).HasColumnName("delivery_address").HasMaxLength(600);
        builder.Property(x => x.DeliveryMethod).HasColumnName("delivery_method").HasMaxLength(40);
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(60);
        builder.Property(x => x.Total).HasColumnName("total");
        builder.Property(x => x.TotalProducts).HasColumnName("total_products");
        builder.Property(x => x.TotalDiscounts).HasColumnName("total_discounts");
        builder.Property(x => x.TotalDiscountByPartner).HasColumnName("total_discount_by_partner");
        builder.Property(x => x.TotalDiscountByRappi).HasColumnName("total_discount_by_rappi");
        builder.Property(x => x.TotalCharges).HasColumnName("total_charges");
        builder.Property(x => x.CookingTimeMinutes).HasColumnName("cooking_time_minutes");
        builder.Property(x => x.RawPayloadJson).HasColumnName("raw_payload_json").HasColumnType("jsonb");
        builder.Property(x => x.LinesJson).HasColumnName("lines_json").HasColumnType("jsonb");
        builder.Property(x => x.DiscountsJson).HasColumnName("discounts_json").HasColumnType("jsonb");
        builder.Property(x => x.ValidationErrorsJson).HasColumnName("validation_errors_json").HasColumnType("jsonb");
        builder.Property(x => x.InternalOrderId).HasColumnName("internal_order_id");
        builder.Property(x => x.AcceptedByUserId).HasColumnName("accepted_by_user_id");
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(x => x.PiiPurgedAt).HasColumnName("pii_purged_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.ConnectionId, x.ExternalOrderId }).IsUnique().HasDatabaseName("ux_external_delivery_order_provider_id");
        builder.HasIndex(x => new { x.BranchId, x.Status }).HasDatabaseName("idx_external_delivery_order_branch_status");
        builder.HasOne(x => x.Connection).WithMany(x => x.ExternalOrders).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InternalOrder).WithMany().HasForeignKey(x => x.InternalOrderId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class IntegrationWebhookEventConfiguration : IEntityTypeConfiguration<IntegrationWebhookEvent>
{
    public void Configure(EntityTypeBuilder<IntegrationWebhookEvent> builder)
    {
        builder.ToTable("integration_webhook_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40);
        builder.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(200);
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80);
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.ConnectionId, x.EventKey }).IsUnique().HasDatabaseName("ux_integration_webhook_event_key");
    }
}

public class RappiMenuPublicationConfiguration : IEntityTypeConfiguration<RappiMenuPublication>
{
    public void Configure(EntityTypeBuilder<RappiMenuPublication> builder)
    {
        builder.ToTable("rappi_menu_publication");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.StoreId).HasColumnName("store_id").HasMaxLength(120);
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40);
        builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(1000);
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.ConnectionId, x.CreatedAt }).HasDatabaseName("idx_rappi_menu_publication_connection");
        builder.HasOne(x => x.Connection).WithMany(x => x.MenuPublications).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RappiAvailabilityStateConfiguration : IEntityTypeConfiguration<RappiAvailabilityState>
{
    public void Configure(EntityTypeBuilder<RappiAvailabilityState> builder)
    {
        builder.ToTable("rappi_availability_state");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.StoreId).HasColumnName("store_id");
        builder.Property(x => x.ProductMappingId).HasColumnName("product_mapping_id");
        builder.Property(x => x.DesiredAvailable).HasColumnName("desired_available");
        builder.Property(x => x.LastSyncedAvailable).HasColumnName("last_synced_available");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        DeliveryAppConnectionConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.StoreId, x.ProductMappingId }).IsUnique().HasDatabaseName("ux_rappi_availability_store_product");
        builder.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductMapping).WithMany().HasForeignKey(x => x.ProductMappingId).OnDelete(DeleteBehavior.Cascade);
    }
}
