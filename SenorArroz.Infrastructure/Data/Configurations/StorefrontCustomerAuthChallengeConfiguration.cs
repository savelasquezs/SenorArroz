using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class StorefrontCustomerAuthChallengeConfiguration : IEntityTypeConfiguration<StorefrontCustomerAuthChallenge>
{
    public void Configure(EntityTypeBuilder<StorefrontCustomerAuthChallenge> builder)
    {
        builder.ToTable("storefront_customer_auth_challenge");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.PublicId).HasColumnName("public_id").IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(10).IsRequired();
        builder.Property(x => x.CodeHmac).HasColumnName("code_hmac").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.ResendAvailableAt).HasColumnName("resend_available_at").IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5);
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(x => x.SessionTokenHash).HasColumnName("session_token_hash").HasMaxLength(64);
        builder.Property(x => x.SessionExpiresAt).HasColumnName("session_expires_at");
        builder.Property(x => x.RequestIpHash).HasColumnName("request_ip_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("uq_storefront_customer_auth_challenge_public_id");
        builder.HasIndex(x => new { x.TenantId, x.Phone, x.CreatedAt }).HasDatabaseName("idx_storefront_customer_auth_challenge_phone");
        builder.HasIndex(x => new { x.TenantId, x.RequestIpHash, x.CreatedAt }).HasDatabaseName("idx_storefront_customer_auth_challenge_ip");
        builder.HasIndex(x => x.SessionTokenHash).HasDatabaseName("idx_storefront_customer_auth_challenge_session");
    }
}
