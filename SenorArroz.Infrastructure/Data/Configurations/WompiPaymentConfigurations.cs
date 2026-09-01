using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class WompiPaymentIntegrationConfiguration : IEntityTypeConfiguration<WompiPaymentIntegration>
{
    public void Configure(EntityTypeBuilder<WompiPaymentIntegration> builder)
    {
        builder.ToTable("wompi_payment_integration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.FinancialAppId).HasColumnName("financial_app_id");
        builder.Property(x => x.ActiveEnvironment).HasColumnName("active_environment").HasMaxLength(20);
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.EstimatedCommissionRate).HasColumnName("estimated_commission_rate").HasPrecision(8, 6);
        builder.Property(x => x.SandboxPublicKey).HasColumnName("sandbox_public_key").HasMaxLength(160);
        builder.Property(x => x.SandboxEncryptedIntegritySecret).HasColumnName("sandbox_encrypted_integrity_secret").HasColumnType("text");
        builder.Property(x => x.SandboxEncryptedEventsSecret).HasColumnName("sandbox_encrypted_events_secret").HasColumnType("text");
        builder.Property(x => x.ProductionPublicKey).HasColumnName("production_public_key").HasMaxLength(160);
        builder.Property(x => x.ProductionEncryptedIntegritySecret).HasColumnName("production_encrypted_integrity_secret").HasColumnType("text");
        builder.Property(x => x.ProductionEncryptedEventsSecret).HasColumnName("production_encrypted_events_secret").HasColumnType("text");
        builder.Property(x => x.LastSandboxWebhookAt).HasColumnName("last_sandbox_webhook_at");
        builder.Property(x => x.LastProductionWebhookAt).HasColumnName("last_production_webhook_at");
        builder.Property(x => x.LastTestedAt).HasColumnName("last_tested_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        Timestamps(builder);
        builder.HasIndex(x => new { x.TenantId, x.BranchId }).IsUnique().HasDatabaseName("ux_wompi_payment_integration_tenant_branch");
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinancialApp).WithMany().HasForeignKey(x => x.FinancialAppId).OnDelete(DeleteBehavior.Restrict);
    }

    internal static void Timestamps<T>(EntityTypeBuilder<T> builder) where T : Domain.Entities.Common.BaseEntity
    {
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}

public sealed class WompiPaymentAttemptConfiguration : IEntityTypeConfiguration<WompiPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<WompiPaymentAttempt> builder)
    {
        builder.ToTable("wompi_payment_attempt");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.IntegrationId).HasColumnName("integration_id");
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100);
        builder.Property(x => x.Environment).HasColumnName("environment").HasMaxLength(20);
        builder.Property(x => x.PublicKeySnapshot).HasColumnName("public_key_snapshot").HasMaxLength(160);
        builder.Property(x => x.IntegritySignature).HasColumnName("integrity_signature").HasMaxLength(64);
        builder.Property(x => x.EncryptedEventsSecretSnapshot).HasColumnName("encrypted_events_secret_snapshot").HasColumnType("text");
        builder.Property(x => x.ExpectedAmountInCents).HasColumnName("expected_amount_in_cents");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        builder.Property(x => x.RequiresManualReview).HasColumnName("requires_manual_review");
        builder.Property(x => x.ManualReviewReason).HasColumnName("manual_review_reason").HasMaxLength(500);
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.AppPaymentId).HasColumnName("app_payment_id");
        WompiPaymentIntegrationConfiguration.Timestamps(builder);
        builder.HasIndex(x => x.Reference).IsUnique().HasDatabaseName("ux_wompi_payment_attempt_reference");
        builder.HasIndex(x => new { x.TenantId, x.OrderId }).HasDatabaseName("ix_wompi_payment_attempt_tenant_order");
        builder.HasIndex(x => new { x.IntegrationId, x.Status }).HasDatabaseName("ix_wompi_payment_attempt_integration_status");
        builder.HasIndex(x => x.AppPaymentId).IsUnique().HasFilter("app_payment_id IS NOT NULL").HasDatabaseName("ux_wompi_payment_attempt_app_payment");
        builder.HasOne(x => x.Order).WithMany(x => x.WompiPaymentAttempts).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Integration).WithMany(x => x.PaymentAttempts).HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AppPayment).WithMany().HasForeignKey(x => x.AppPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WompiProviderTransactionConfiguration : IEntityTypeConfiguration<WompiProviderTransaction>
{
    public void Configure(EntityTypeBuilder<WompiProviderTransaction> builder)
    {
        builder.ToTable("wompi_provider_transaction");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.PaymentAttemptId).HasColumnName("payment_attempt_id");
        builder.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(120);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(40);
        builder.Property(x => x.AmountInCents).HasColumnName("amount_in_cents");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        builder.Property(x => x.ObservedAt).HasColumnName("observed_at");
        WompiPaymentIntegrationConfiguration.Timestamps(builder);
        builder.HasIndex(x => x.ProviderTransactionId).IsUnique().HasDatabaseName("ux_wompi_provider_transaction_id");
        builder.HasOne(x => x.PaymentAttempt).WithMany(x => x.ProviderTransactions).HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WompiWebhookEventConfiguration : IEntityTypeConfiguration<WompiWebhookEvent>
{
    public void Configure(EntityTypeBuilder<WompiWebhookEvent> builder)
    {
        builder.ToTable("wompi_webhook_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.IntegrationId).HasColumnName("integration_id");
        builder.Property(x => x.Environment).HasColumnName("environment").HasMaxLength(20);
        builder.Property(x => x.EventFingerprint).HasColumnName("event_fingerprint").HasMaxLength(64);
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        builder.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(120);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        WompiPaymentIntegrationConfiguration.Timestamps(builder);
        builder.HasIndex(x => x.EventFingerprint).IsUnique().HasDatabaseName("ux_wompi_webhook_event_fingerprint");
        builder.HasOne(x => x.Integration).WithMany().HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentNotificationOutboxMessageConfiguration : IEntityTypeConfiguration<PaymentNotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<PaymentNotificationOutboxMessage> builder)
    {
        builder.ToTable("payment_notification_outbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(60);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        WompiPaymentIntegrationConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("ix_payment_notification_outbox_pending");
        builder.HasIndex(x => new { x.OrderId, x.EventType }).IsUnique().HasDatabaseName("ux_payment_notification_outbox_order_event");
        builder.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
