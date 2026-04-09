using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class UserDeviceTokenConfiguration : IEntityTypeConfiguration<UserDeviceToken>
{
    public void Configure(EntityTypeBuilder<UserDeviceToken> builder)
    {
        builder.ToTable("user_device_token");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.Token).HasColumnName("token").HasMaxLength(512).IsRequired();
        builder.Property(t => t.Platform).HasColumnName("platform").HasMaxLength(20).HasDefaultValue("android");
        builder.Property(t => t.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("NOW()");

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Un usuario puede tener varios dispositivos pero no duplicar el mismo token
        builder.HasIndex(t => t.Token).IsUnique().HasDatabaseName("uq_user_device_token_token");
        builder.HasIndex(t => t.UserId).HasDatabaseName("idx_user_device_token_user");
    }
}
