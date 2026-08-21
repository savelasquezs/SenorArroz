using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryDeviceEventConfiguration : IEntityTypeConfiguration<DeliveryDeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeliveryDeviceEvent> builder)
    {
        builder.ToTable("delivery_device_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("bigint").ValueGeneratedOnAdd();
        builder.Property(x => x.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(x => x.WorkSessionId).HasColumnName("work_session_id").IsRequired();
        builder.Property(x => x.ClientEventId).HasColumnName("client_event_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasConversion(
            value => ToSnakeCase(value.ToString()),
            value => Enum.Parse<DeliveryDeviceEventType>(ToPascalCase(value), true))
            .HasMaxLength(50).IsRequired();
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
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Deliveryman).WithMany().HasForeignKey(x => x.DeliverymanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkSession).WithMany().HasForeignKey(x => x.WorkSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ClientEventId).IsUnique().HasFilter("client_event_id IS NOT NULL")
            .HasDatabaseName("uq_delivery_device_event_client_id");
        builder.HasIndex(x => new { x.WorkSessionId, x.RecordedAt })
            .HasDatabaseName("idx_delivery_device_event_session_recorded");
        builder.HasIndex(x => new { x.DeliverymanId, x.RecordedAt })
            .HasDatabaseName("idx_delivery_device_event_deliveryman_recorded");
    }

    private static string ToSnakeCase(string input) => string.Concat(
        input.Select((character, index) => char.IsUpper(character) && index > 0
            ? $"_{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private static string ToPascalCase(string input) => string.Concat(
        input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
