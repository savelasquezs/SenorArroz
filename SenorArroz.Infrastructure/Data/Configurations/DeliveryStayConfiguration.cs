using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryStayConfiguration : IEntityTypeConfiguration<DeliveryStay>
{
    public void Configure(EntityTypeBuilder<DeliveryStay> builder)
    {
        builder.ToTable("delivery_stay");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("bigint").ValueGeneratedOnAdd();
        builder.Property(x => x.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(x => x.WorkSessionId).HasColumnName("work_session_id").IsRequired();
        builder.Property(x => x.DeliveryRouteId).HasColumnName("delivery_route_id");
        builder.Property(x => x.NearestOrderId).HasColumnName("nearest_order_id");
        builder.Property(x => x.FirstLocationId).HasColumnName("first_location_id").IsRequired();
        builder.Property(x => x.LastLocationId).HasColumnName("last_location_id").IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.EndedAt).HasColumnName("ended_at").IsRequired();
        builder.Property(x => x.DurationSeconds).HasColumnName("duration_seconds").IsRequired();
        builder.Property(x => x.CenterLatitude).HasColumnName("center_latitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.CenterLongitude).HasColumnName("center_longitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.RadiusMeters).HasColumnName("radius_meters").IsRequired();
        builder.Property(x => x.AverageAccuracyMeters).HasColumnName("average_accuracy_meters").IsRequired();
        builder.Property(x => x.DistanceToBranchMeters).HasColumnName("distance_to_branch_meters");
        builder.Property(x => x.DistanceToNearestOrderMeters).HasColumnName("distance_to_nearest_order_meters");
        builder.Property(x => x.PointCount).HasColumnName("point_count").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(x => x.Deliveryman).WithMany().HasForeignKey(x => x.DeliverymanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkSession).WithMany().HasForeignKey(x => x.WorkSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DeliveryRoute).WithMany().HasForeignKey(x => x.DeliveryRouteId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.NearestOrder).WithMany().HasForeignKey(x => x.NearestOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.WorkSessionId, x.FirstLocationId }).IsUnique()
            .HasDatabaseName("uq_delivery_stay_session_first_location");
        builder.HasIndex(x => new { x.WorkSessionId, x.StartedAt })
            .HasDatabaseName("idx_delivery_stay_session_started");
        builder.HasIndex(x => new { x.DeliverymanId, x.StartedAt })
            .HasDatabaseName("idx_delivery_stay_deliveryman_started");
        builder.HasIndex(x => x.DeliveryRouteId).HasDatabaseName("idx_delivery_stay_route");
    }
}
