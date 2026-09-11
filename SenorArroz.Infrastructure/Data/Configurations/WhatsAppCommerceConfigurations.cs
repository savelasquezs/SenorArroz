using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class WhatsAppChannelSettingConfiguration : IEntityTypeConfiguration<WhatsAppChannelSetting>
{
    public void Configure(EntityTypeBuilder<WhatsAppChannelSetting> builder)
    {
        builder.ToTable("whatsapp_channel_setting");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.PublicId).HasColumnName("public_id");
        builder.Property(x => x.PhoneNumberId).HasColumnName("phone_number_id").HasMaxLength(64);
        builder.Property(x => x.BusinessAccountId).HasColumnName("business_account_id").HasMaxLength(64);
        builder.Property(x => x.DisplayPhoneNumber).HasColumnName("display_phone_number").HasMaxLength(32);
        builder.Property(x => x.AccessToken).HasColumnName("access_token");
        builder.Property(x => x.WebhookVerifyToken).HasColumnName("webhook_verify_token").HasMaxLength(255);
        builder.Property(x => x.AppSecret).HasColumnName("app_secret").HasMaxLength(255);
        builder.Property(x => x.FlowId).HasColumnName("flow_id").HasMaxLength(64);
        builder.Property(x => x.FlowJsonVersion).HasColumnName("flow_json_version").HasMaxLength(16);
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.IsVerified).HasColumnName("is_verified");
        builder.Property(x => x.FlowEnabled).HasColumnName("flow_enabled");
        builder.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
        builder.Property(x => x.AwayMessageEnabled).HasColumnName("away_message_enabled");
        builder.Property(x => x.AwayMessageText).HasColumnName("away_message_text").HasMaxLength(3500);
        Timestamps(builder);
        builder.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("ux_whatsapp_channel_setting_public_id");
        builder.HasIndex(x => x.PhoneNumberId).IsUnique().HasDatabaseName("ux_whatsapp_channel_setting_phone_number");
        builder.HasIndex(x => new { x.TenantId, x.IsActive }).HasDatabaseName("ix_whatsapp_channel_setting_tenant_active");
    }

    internal static void Timestamps<T>(EntityTypeBuilder<T> builder) where T : Domain.Entities.Common.BaseEntity
    {
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}

public sealed class TenantAiSettingConfiguration : IEntityTypeConfiguration<TenantAiSetting>
{
    public void Configure(EntityTypeBuilder<TenantAiSetting> builder)
    {
        builder.ToTable("tenant_ai_setting");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40);
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(120);
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.Temperature).HasColumnName("temperature");
        builder.Property(x => x.MaxContextMessages).HasColumnName("max_context_messages");
        builder.Property(x => x.LastTestedAt).HasColumnName("last_tested_at");
        builder.Property(x => x.IsVerified).HasColumnName("is_verified");
        builder.Property(x => x.AssistantName).HasColumnName("assistant_name").HasMaxLength(200);
        builder.Property(x => x.PromptObjective).HasColumnName("prompt_objective").HasMaxLength(4000);
        builder.Property(x => x.PromptPersonality).HasColumnName("prompt_personality").HasMaxLength(2000);
        builder.Property(x => x.PromptRequiredRules).HasColumnName("prompt_required_rules").HasMaxLength(8000);
        builder.Property(x => x.PromptFixedBranchInfo).HasColumnName("prompt_fixed_branch_info").HasMaxLength(8000);
        builder.Property(x => x.PromptAdditionalInstructions).HasColumnName("prompt_additional_instructions").HasMaxLength(8000);
        builder.Property(x => x.TransferMessage).HasColumnName("transfer_message").HasMaxLength(1000);
        Timestamps(builder);
        builder.HasIndex(x => x.TenantId).IsUnique().HasDatabaseName("ux_tenant_ai_setting_tenant");
    }

    private static void Timestamps<T>(EntityTypeBuilder<T> builder) where T : Domain.Entities.Common.BaseEntity =>
        WhatsAppChannelSettingConfiguration.Timestamps(builder);
}

public sealed class WhatsAppCommerceSessionConfiguration : IEntityTypeConfiguration<WhatsAppCommerceSession>
{
    public void Configure(EntityTypeBuilder<WhatsAppCommerceSession> builder)
    {
        builder.ToTable("whatsapp_commerce_session");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.ChannelSettingId).HasColumnName("channel_setting_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.FlowTokenHash).HasColumnName("flow_token_hash").HasMaxLength(64);
        builder.Property(x => x.StateJson).HasColumnName("state_json").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(80);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        WhatsAppChannelSettingConfiguration.Timestamps(builder);
        builder.HasIndex(x => x.FlowTokenHash).IsUnique().HasDatabaseName("ux_whatsapp_commerce_session_flow_token");
        builder.HasIndex(x => x.CorrelationId).IsUnique().HasDatabaseName("ux_whatsapp_commerce_session_correlation");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("ux_whatsapp_commerce_session_idempotency");
        builder.HasIndex(x => new { x.ConversationId, x.Status, x.ExpiresAt }).HasDatabaseName("ix_whatsapp_commerce_session_resume");
        builder.HasOne(x => x.ChannelSetting).WithMany(x => x.CommerceSessions).HasForeignKey(x => x.ChannelSettingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class WhatsAppFlowExchangeConfiguration : IEntityTypeConfiguration<WhatsAppFlowExchange>
{
    public void Configure(EntityTypeBuilder<WhatsAppFlowExchange> builder)
    {
        builder.ToTable("whatsapp_flow_exchange");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64);
        builder.Property(x => x.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
        WhatsAppChannelSettingConfiguration.Timestamps(builder);
        builder.HasIndex(x => new { x.SessionId, x.RequestFingerprint }).IsUnique().HasDatabaseName("ux_whatsapp_flow_exchange_request");
        builder.HasOne(x => x.Session).WithMany(x => x.Exchanges).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WhatsAppCommerceSessionTokenConfiguration : IEntityTypeConfiguration<WhatsAppCommerceSessionToken>
{
    public void Configure(EntityTypeBuilder<WhatsAppCommerceSessionToken> builder)
    {
        builder.ToTable("whatsapp_commerce_session_token");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        WhatsAppChannelSettingConfiguration.Timestamps(builder);
        builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_whatsapp_commerce_session_token_hash");
        builder.HasIndex(x => new { x.SessionId, x.ExpiresAt }).HasDatabaseName("ix_whatsapp_commerce_session_token_active");
        builder.HasOne(x => x.Session).WithMany(x => x.Tokens).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WhatsAppCommerceOutboxMessageConfiguration : IEntityTypeConfiguration<WhatsAppCommerceOutboxMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppCommerceOutboxMessage> builder)
    {
        builder.ToTable("whatsapp_commerce_outbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ChannelSettingId).HasColumnName("channel_setting_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(160);
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(4096);
        builder.Property(x => x.ButtonText).HasColumnName("button_text").HasMaxLength(40);
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2000);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(x => x.EventKey).IsUnique().HasDatabaseName("ux_whatsapp_commerce_outbox_event");
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("ix_whatsapp_commerce_outbox_pending");
        builder.HasOne(x => x.ChannelSetting).WithMany().HasForeignKey(x => x.ChannelSettingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WhatsAppCommerceEventConfiguration : IEntityTypeConfiguration<WhatsAppCommerceEvent>
{
    public void Configure(EntityTypeBuilder<WhatsAppCommerceEvent> builder)
    {
        builder.ToTable("whatsapp_commerce_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.EventKey).HasColumnName("event_key").HasMaxLength(180);
        builder.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(80);
        builder.Property(x => x.Screen).HasColumnName("screen").HasMaxLength(40);
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id").HasMaxLength(100);
        WhatsAppChannelSettingConfiguration.Timestamps(builder);
        builder.HasIndex(x => x.EventKey).IsUnique().HasDatabaseName("ux_whatsapp_commerce_event_key");
        builder.HasIndex(x => new { x.TenantId, x.EventName, x.CreatedAt }).HasDatabaseName("ix_whatsapp_commerce_event_metrics");
        builder.HasIndex(x => new { x.SessionId, x.CreatedAt }).HasDatabaseName("ix_whatsapp_commerce_event_session");
        builder.HasOne(x => x.Session).WithMany(x => x.Events).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
    }
}
