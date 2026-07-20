using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliverymanLocationConfiguration : IEntityTypeConfiguration<DeliverymanLocation>
{
    public void Configure(EntityTypeBuilder<DeliverymanLocation> builder)
    {
        builder.ToTable("deliveryman_location");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(e => e.WorkSessionId).HasColumnName("work_session_id");
        builder.Property(e => e.DeliveryRouteId).HasColumnName("delivery_route_id");
        builder.Property(e => e.ClientPointId).HasColumnName("client_point_id");
        builder.Property(e => e.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(e => e.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,6)").IsRequired();
        builder.Property(e => e.AccuracyMeters).HasColumnName("accuracy_meters");
        builder.Property(e => e.HeadingDegrees).HasColumnName("heading_degrees");
        builder.Property(e => e.BatteryLevelPercent).HasColumnName("battery_level_percent");
        builder.Property(e => e.InternetAvailable).HasColumnName("internet_available");
        builder.Property(e => e.GpsEnabled).HasColumnName("gps_enabled");
        builder.Property(e => e.TrackingMode).HasColumnName("tracking_mode").HasConversion(
            value => value == null ? null : ToSnakeCase(value.Value.ToString()),
            value => value == null ? null : Enum.Parse<DeliveryTrackingMode>(ToPascalCase(value), true));
        builder.Property(e => e.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(e => e.SyncedAt).HasColumnName("synced_at");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(e => e.Deliveryman)
            .WithMany()
            .HasForeignKey(e => e.DeliverymanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DeliveryRoute)
            .WithMany()
            .HasForeignKey(e => e.DeliveryRouteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.WorkSession)
            .WithMany(s => s.Locations)
            .HasForeignKey(e => e.WorkSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.DeliverymanId).HasDatabaseName("idx_dloc_deliveryman");
        builder.HasIndex(e => e.WorkSessionId).HasDatabaseName("idx_dloc_work_session");
        builder.HasIndex(e => e.DeliveryRouteId).HasDatabaseName("idx_dloc_route");
        builder.HasIndex(e => e.RecordedAt).HasDatabaseName("idx_dloc_recorded");
        builder.HasIndex(e => e.ClientPointId)
            .IsUnique()
            .HasFilter("client_point_id IS NOT NULL")
            .HasDatabaseName("uq_dloc_client_point_id");
    }

    private static string ToSnakeCase(string input) => string.Concat(
        input.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    private static string ToPascalCase(string input) => string.Concat(
        input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
