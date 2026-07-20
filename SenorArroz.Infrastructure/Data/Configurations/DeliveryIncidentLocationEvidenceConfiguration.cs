using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryIncidentLocationEvidenceConfiguration : IEntityTypeConfiguration<DeliveryIncidentLocationEvidence>
{
    public void Configure(EntityTypeBuilder<DeliveryIncidentLocationEvidence> builder)
    {
        builder.ToTable("delivery_incident_location_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("bigint").ValueGeneratedOnAdd();
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(x => x.SourceLocationId).HasColumnName("source_location_id").IsRequired();
        builder.Property(x => x.ClientPointId).HasColumnName("client_point_id");
        builder.Property(x => x.IsCorePoint).HasColumnName("is_core_point").IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(x => x.AccuracyMeters).HasColumnName("accuracy_meters");
        builder.Property(x => x.HeadingDegrees).HasColumnName("heading_degrees");
        builder.Property(x => x.BatteryLevelPercent).HasColumnName("battery_level_percent");
        builder.Property(x => x.InternetAvailable).HasColumnName("internet_available");
        builder.Property(x => x.GpsEnabled).HasColumnName("gps_enabled");
        builder.Property(x => x.TrackingMode).HasColumnName("tracking_mode").HasMaxLength(30).HasConversion(
            value => value == null ? null : ToSnakeCase(value.Value.ToString()),
            value => value == null ? null : Parse<DeliveryTrackingMode>(value));
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.SyncedAt).HasColumnName("synced_at");

        builder.HasOne(x => x.Incident).WithMany(x => x.LocationEvidence).HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.IncidentId, x.SourceLocationId }).IsUnique()
            .HasDatabaseName("uq_delivery_incident_location_source");
        builder.HasIndex(x => new { x.IncidentId, x.RecordedAt })
            .HasDatabaseName("idx_delivery_incident_location_recorded");
    }

    private static string ToSnakeCase(string input) => string.Concat(
        input.Select((character, index) => char.IsUpper(character) && index > 0
            ? $"_{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private static T Parse<T>(string input) where T : struct, Enum => Enum.Parse<T>(string.Concat(
        input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])), true);
}
