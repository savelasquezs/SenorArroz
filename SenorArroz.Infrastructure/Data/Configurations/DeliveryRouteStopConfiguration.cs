using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryRouteStopConfiguration : IEntityTypeConfiguration<DeliveryRouteStop>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteStop> builder)
    {
        builder.ToTable("delivery_route_stop");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.DeliveryRouteId).HasColumnName("delivery_route_id").IsRequired();
        builder.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(e => e.StopSequence).HasColumnName("stop_sequence");
        builder.Property(e => e.AddressSnapshotText).HasColumnName("address_snapshot_text");
        builder.Property(e => e.RequiresComplexAccessBuffer).HasColumnName("requires_complex_access_buffer");
        builder.Property(e => e.ComplexAccessMatchTerm).HasColumnName("complex_access_match_term").HasMaxLength(64);
        builder.Property(e => e.ComplexAccessBonusSeconds).HasColumnName("complex_access_bonus_seconds");
        builder.Property(e => e.ArrivalCandidateAtUtc).HasColumnName("arrival_candidate_at_utc");
        builder.Property(e => e.ArrivalConfirmedAtUtc).HasColumnName("arrival_confirmed_at_utc");
        builder.Property(e => e.ArrivalEvidenceCount).HasColumnName("arrival_evidence_count").HasDefaultValue(0);
        builder.Property(e => e.ArrivalLastSeenAtUtc).HasColumnName("arrival_last_seen_at_utc");
        builder.Property(e => e.ClosestDistanceMeters).HasColumnName("closest_distance_meters");
        builder.Property(e => e.AutoDeliveredAtUtc).HasColumnName("auto_delivered_at_utc");
        builder.Property(e => e.AutoDeliveryTriggerLocationId).HasColumnName("auto_delivery_trigger_location_id");
        builder.Property(e => e.AutoDeliveryDepartureDistanceMeters)
            .HasColumnName("auto_delivery_departure_distance_meters");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(e => e.DeliveryRouteId).HasDatabaseName("idx_delivery_route_stop_route");
        builder.HasIndex(e => e.OrderId).IsUnique().HasDatabaseName("uq_delivery_route_stop_order");

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
