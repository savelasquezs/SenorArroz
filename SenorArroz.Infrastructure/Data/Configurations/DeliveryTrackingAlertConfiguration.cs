using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryTrackingAlertConfiguration : IEntityTypeConfiguration<DeliveryTrackingAlert>
{
    public void Configure(EntityTypeBuilder<DeliveryTrackingAlert> builder)
    {
        builder.ToTable("delivery_tracking_alert");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("bigint").ValueGeneratedOnAdd();
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(x => x.WorkSessionId).HasColumnName("work_session_id");
        builder.Property(x => x.IncidentId).HasColumnName("incident_id");
        builder.Property(x => x.SourceDeviceEventId).HasColumnName("source_device_event_id");
        builder.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(160).IsRequired();
        builder.Property(x => x.AlertType).HasColumnName("alert_type").HasMaxLength(50)
            .HasConversion(value => ToSnakeCase(value.ToString()), value => Parse<DeliveryTrackingAlertType>(value)).IsRequired();
        builder.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(30)
            .HasConversion(value => ToSnakeCase(value.ToString()), value => Parse<DeliveryTrackingAlertSeverity>(value)).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20)
            .HasConversion(value => ToSnakeCase(value.ToString()), value => Parse<DeliveryTrackingAlertStatus>(value)).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.LastOccurredAt).HasColumnName("last_occurred_at").IsRequired();
        builder.Property(x => x.OccurrenceCount).HasColumnName("occurrence_count").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(x => x.ResolutionReason).HasColumnName("resolution_reason").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.DeduplicationKey).IsUnique().HasDatabaseName("uq_delivery_tracking_alert_dedup");
        builder.HasIndex(x => x.SourceDeviceEventId).IsUnique().HasFilter("source_device_event_id IS NOT NULL")
            .HasDatabaseName("uq_delivery_tracking_alert_device_event");
        builder.HasIndex(x => x.IncidentId).IsUnique().HasFilter("incident_id IS NOT NULL")
            .HasDatabaseName("uq_delivery_tracking_alert_incident");
        builder.HasIndex(x => new { x.BranchId, x.Status, x.Severity, x.OccurredAt })
            .HasDatabaseName("idx_delivery_tracking_alert_admin");
        builder.HasIndex(x => new { x.WorkSessionId, x.AlertType, x.Status })
            .HasDatabaseName("idx_delivery_tracking_alert_session");
    }

    private static string ToSnakeCase(string input) => string.Concat(
        input.Select((character, index) => char.IsUpper(character) && index > 0
            ? $"_{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private static T Parse<T>(string input) where T : struct, Enum => Enum.Parse<T>(string.Concat(
        input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])), true);
}
