using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryTrackingIncidentConfiguration : IEntityTypeConfiguration<DeliveryTrackingIncident>
{
    public void Configure(EntityTypeBuilder<DeliveryTrackingIncident> builder)
    {
        builder.ToTable("delivery_tracking_incident");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("bigint").ValueGeneratedOnAdd();
        builder.Property(x => x.IncidentType).HasColumnName("incident_type").HasMaxLength(30)
            .HasConversion(value => ToSnakeCase(value.ToString()), value => Parse<DeliveryTrackingIncidentType>(value))
            .IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(x => x.WorkSessionId).HasColumnName("work_session_id").IsRequired();
        builder.Property(x => x.DeliveryStayId).HasColumnName("delivery_stay_id");
        builder.Property(x => x.DeliveryRouteId).HasColumnName("delivery_route_id");
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.StayClassification).HasColumnName("stay_classification").HasMaxLength(40)
            .HasConversion(
                value => value == null ? null : ToSnakeCase(value.Value.ToString()),
                value => value == null ? null : Parse<DeliveryStayClassification>(value));
        builder.Property(x => x.ClassificationReason).HasColumnName("classification_reason").HasMaxLength(300);
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.EndedAt).HasColumnName("ended_at").IsRequired();
        builder.Property(x => x.DurationSeconds).HasColumnName("duration_seconds").IsRequired();
        builder.Property(x => x.CenterLatitude).HasColumnName("center_latitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.CenterLongitude).HasColumnName("center_longitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.RadiusMeters).HasColumnName("radius_meters").IsRequired();
        builder.Property(x => x.AverageAccuracyMeters).HasColumnName("average_accuracy_meters").IsRequired();
        builder.Property(x => x.DistanceToBranchMeters).HasColumnName("distance_to_branch_meters");
        builder.Property(x => x.DistanceToOrderMeters).HasColumnName("distance_to_order_meters");
        builder.Property(x => x.OrderAddressSnapshot).HasColumnName("order_address_snapshot").HasMaxLength(500);
        builder.Property(x => x.OrderLatitudeSnapshot).HasColumnName("order_latitude_snapshot").HasColumnType("numeric(10,6)");
        builder.Property(x => x.OrderLongitudeSnapshot).HasColumnName("order_longitude_snapshot").HasColumnType("numeric(10,6)");
        builder.Property(x => x.OrderStatusSnapshot).HasColumnName("order_status_snapshot").HasMaxLength(50);
        builder.Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at").IsRequired();
        builder.Property(x => x.EvidenceCapturedAt).HasColumnName("evidence_captured_at").IsRequired();
        builder.Property(x => x.EvidenceComplete).HasColumnName("evidence_complete").IsRequired();
        builder.Property(x => x.ReviewStatus).HasColumnName("review_status").HasMaxLength(50)
            .HasConversion(value => ToSnakeCase(value.ToString()), value => Parse<DeliveryIncidentReviewStatus>(value))
            .IsRequired();
        builder.Property(x => x.FinalClassification).HasColumnName("final_classification").HasMaxLength(40)
            .HasConversion(
                value => value == null ? null : ToSnakeCase(value.Value.ToString()),
                value => value == null ? null : Parse<DeliveryStayClassification>(value));
        builder.Property(x => x.AdminNotes).HasColumnName("admin_notes").HasMaxLength(2000);
        builder.Property(x => x.DeliverymanExplanation).HasColumnName("deliveryman_explanation").HasMaxLength(2000);
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.DeliveryStayId).IsUnique().HasFilter("delivery_stay_id IS NOT NULL")
            .HasDatabaseName("uq_delivery_tracking_incident_stay");
        builder.HasIndex(x => new { x.BranchId, x.StartedAt })
            .HasDatabaseName("idx_delivery_tracking_incident_branch_started");
        builder.HasIndex(x => new { x.WorkSessionId, x.StartedAt })
            .HasDatabaseName("idx_delivery_tracking_incident_session_started");
        builder.HasIndex(x => new { x.BranchId, x.ReviewStatus, x.StartedAt })
            .HasDatabaseName("idx_delivery_tracking_incident_review");
    }

    private static string ToSnakeCase(string input) => string.Concat(
        input.Select((character, index) => char.IsUpper(character) && index > 0
            ? $"_{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private static T Parse<T>(string input) where T : struct, Enum => Enum.Parse<T>(string.Concat(
        input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])), true);
}
