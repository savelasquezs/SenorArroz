using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryIncidentDeviceEventEvidenceConfiguration : IEntityTypeConfiguration<DeliveryIncidentDeviceEventEvidence>
{
    public void Configure(EntityTypeBuilder<DeliveryIncidentDeviceEventEvidence> builder)
    {
        builder.ToTable("delivery_incident_device_event_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("bigint").ValueGeneratedOnAdd();
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(x => x.SourceDeviceEventId).HasColumnName("source_device_event_id").IsRequired();
        builder.Property(x => x.ClientEventId).HasColumnName("client_event_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(50)
            .HasConversion(value => ToSnakeCase(value.ToString()), value => Parse<DeliveryDeviceEventType>(value))
            .IsRequired();
        builder.Property(x => x.BatteryLevelPercent).HasColumnName("battery_level_percent");
        builder.Property(x => x.InternetAvailable).HasColumnName("internet_available");
        builder.Property(x => x.GpsEnabled).HasColumnName("gps_enabled");
        builder.Property(x => x.LocationPermissionGranted).HasColumnName("location_permission_granted");
        builder.Property(x => x.Details).HasColumnName("details").HasMaxLength(500);
        builder.Property(x => x.OfflineLocationCount).HasColumnName("offline_location_count");
        builder.Property(x => x.OfflineStartedAt).HasColumnName("offline_started_at");
        builder.Property(x => x.OfflineEndedAt).HasColumnName("offline_ended_at");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.SyncedAt).HasColumnName("synced_at").IsRequired();

        builder.HasOne(x => x.Incident).WithMany(x => x.DeviceEventEvidence).HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.IncidentId, x.SourceDeviceEventId }).IsUnique()
            .HasDatabaseName("uq_delivery_incident_device_event_source");
        builder.HasIndex(x => new { x.IncidentId, x.RecordedAt })
            .HasDatabaseName("idx_delivery_incident_device_event_recorded");
    }

    private static string ToSnakeCase(string input) => string.Concat(
        input.Select((character, index) => char.IsUpper(character) && index > 0
            ? $"_{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private static T Parse<T>(string input) where T : struct, Enum => Enum.Parse<T>(string.Concat(
        input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])), true);
}
