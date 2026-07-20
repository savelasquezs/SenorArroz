using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryAuthorizedPlaceConfiguration : IEntityTypeConfiguration<DeliveryAuthorizedPlace>
{
    public void Configure(EntityTypeBuilder<DeliveryAuthorizedPlace> builder)
    {
        builder.ToTable("delivery_authorized_place");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.RadiusMeters).HasColumnName("radius_meters").IsRequired();
        builder.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BranchId, x.Active })
            .HasDatabaseName("idx_delivery_authorized_place_branch_active");
    }
}
