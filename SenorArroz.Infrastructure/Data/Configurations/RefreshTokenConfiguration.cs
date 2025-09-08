using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_token");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(rt => rt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(rt => rt.Token)
            .HasColumnName("token")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(rt => rt.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(rt => rt.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false);

        builder.Property(rt => rt.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(rt => rt.ReplacedByToken)
            .HasColumnName("replaced_by_token")
            .HasMaxLength(500);

        builder.Property(rt => rt.RevokedByIp)
            .HasColumnName("revoked_by_ip")
            .HasMaxLength(45);

        builder.Property(rt => rt.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(rt => rt.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // Relationships
        builder.HasOne(rt => rt.User)
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(rt => rt.Token)
            .IsUnique()
            .HasDatabaseName("idx_refresh_token_token");

        builder.HasIndex(rt => rt.UserId)
            .HasDatabaseName("idx_refresh_token_user_id");

        builder.HasIndex(rt => rt.ExpiresAt)
            .HasDatabaseName("idx_refresh_token_expires_at");

        builder.HasIndex(rt => new { rt.UserId, rt.IsRevoked })
            .HasDatabaseName("idx_refresh_token_user_active");
    }
}