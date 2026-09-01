using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branch");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.BusinessName).HasColumnName("business_name").HasMaxLength(150);
        builder.Property(b => b.Nit).HasColumnName("nit").HasMaxLength(32);
        builder.Property(b => b.Address).HasColumnName("address").HasMaxLength(200).IsRequired();
        builder.Property(b => b.Phone1).HasColumnName("phone1").HasMaxLength(10).IsRequired();
        builder.Property(b => b.Phone2).HasColumnName("phone2").HasMaxLength(10);

        builder.Property(b => b.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,6)");
        builder.Property(b => b.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,6)");
        builder.Property(b => b.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(b => b.StorefrontTakenByUserId).HasColumnName("storefront_taken_by_user_id");

        builder.Property(b => b.MaxFreeDeliveryDiscount).HasColumnName("max_free_delivery_discount").HasDefaultValue(3000);
        builder.Property(b => b.PosCopyEtaMinMinutes)
            .HasColumnName("pos_copy_eta_minutes")
            .HasDefaultValue(30);
        builder.Property(b => b.PosCopyEtaRangeMinutes)
            .HasColumnName("pos_copy_eta_range_minutes")
            .HasDefaultValue(15);
        builder.Property(b => b.DeliveryTrackingAutoCloseTime)
            .HasColumnName("delivery_tracking_auto_close_time")
            .HasDefaultValue(new TimeOnly(21, 0));
        builder.Property(b => b.DeliveryTrackingLightIntervalSeconds)
            .HasColumnName("delivery_tracking_light_interval_seconds")
            .HasDefaultValue(300);
        builder.Property(b => b.DeliveryTrackingActiveIntervalSeconds)
            .HasColumnName("delivery_tracking_active_interval_seconds")
            .HasDefaultValue(30);
        builder.Property(b => b.DeliveryTrackingStayThresholdMinutes)
            .HasColumnName("delivery_tracking_stay_threshold_minutes")
            .HasDefaultValue(10);
        builder.Property(b => b.DeliveryTrackingStayRadiusMeters)
            .HasColumnName("delivery_tracking_stay_radius_meters")
            .HasDefaultValue(50);
        builder.Property(b => b.DeliveryTrackingAllowedDistanceMeters)
            .HasColumnName("delivery_tracking_allowed_distance_meters")
            .HasDefaultValue(50);
        builder.Property(b => b.DeliveryTrackingLocationRetentionDays)
            .HasColumnName("delivery_tracking_location_retention_days")
            .HasDefaultValue(3);
        builder.Property(b => b.DeliveryTrackingIncidentRetentionDays)
            .HasColumnName("delivery_tracking_incident_retention_days")
            .HasDefaultValue(15);
        builder.Property(b => b.DeliveryAutoCompleteEnabled)
            .HasColumnName("delivery_auto_complete_enabled")
            .HasDefaultValue(true);
        builder.Property(b => b.DeliveryAutoCompleteArrivalRadiusMeters)
            .HasColumnName("delivery_auto_complete_arrival_radius_meters")
            .HasDefaultValue(50);
        builder.Property(b => b.DeliveryAutoCompleteDepartureRadiusMeters)
            .HasColumnName("delivery_auto_complete_departure_radius_meters")
            .HasDefaultValue(120);
        builder.Property(b => b.DeliveryAutoCompleteMinPresenceSeconds)
            .HasColumnName("delivery_auto_complete_min_presence_seconds")
            .HasDefaultValue(15);
        builder.Property(b => b.MenuImageUrl1).HasColumnName("menu_image_url_1").HasMaxLength(2000);
        builder.Property(b => b.MenuImageUrl2).HasColumnName("menu_image_url_2").HasMaxLength(2000);

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        ;
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
    .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore); ;
    }
}
