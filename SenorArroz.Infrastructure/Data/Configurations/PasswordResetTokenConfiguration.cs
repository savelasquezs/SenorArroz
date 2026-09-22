using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_token");

        builder.HasKey(prt => prt.Id);

        builder.Property(prt => prt.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(prt => prt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(prt => prt.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(prt => prt.Token)
            .HasColumnName("token")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(prt => prt.Email)
            .HasColumnName("email")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(prt => prt.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(prt => prt.IsUsed)
            .HasColumnName("is_used")
            .HasDefaultValue(false);

        builder.Property(prt => prt.UsedAt)
            .HasColumnName("used_at");

        builder.Property(prt => prt.UsedByIp)
            .HasColumnName("used_by_ip")
            .HasMaxLength(45);

        builder.Property(prt => prt.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(prt => prt.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // Relationships
        builder.HasOne(prt => prt.User)
            .WithMany(u => u.PasswordResetTokens)
            .HasForeignKey(prt => new { prt.TenantId, prt.UserId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(prt => prt.Tenant)
            .WithMany(t => t.PasswordResetTokens)
            .HasForeignKey(prt => prt.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(prt => prt.Token)
            .IsUnique()
            .HasDatabaseName("idx_password_reset_token_token");

        builder.HasIndex(prt => prt.UserId)
            .HasDatabaseName("idx_password_reset_token_user_id");

        builder.HasIndex(prt => new { prt.TenantId, prt.UserId })
            .HasDatabaseName("idx_password_reset_token_tenant_user");

        builder.HasIndex(prt => prt.Email)
            .HasDatabaseName("idx_password_reset_token_email");

        builder.HasIndex(prt => prt.ExpiresAt)
            .HasDatabaseName("idx_password_reset_token_expires_at");

        builder.HasIndex(prt => new { prt.UserId, prt.IsUsed })
            .HasDatabaseName("idx_password_reset_token_user_used");
    }
}
