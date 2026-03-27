using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryRouteConfiguration : IEntityTypeConfiguration<DeliveryRoute>
{
    public void Configure(EntityTypeBuilder<DeliveryRoute> builder)
    {
        builder.ToTable("delivery_route");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion(
            v => ToSnakeCase(v.ToString()),
            v => Enum.Parse<DeliveryRouteStatus>(ToPascalCase(v), true))
            .IsRequired();

        builder.Property(e => e.LastAssignmentAtUtc).HasColumnName("last_assignment_at_utc");
        builder.Property(e => e.RouteStartedAtUtc).HasColumnName("route_started_at_utc");
        builder.Property(e => e.PlannedDistanceMeters).HasColumnName("planned_distance_meters");
        builder.Property(e => e.ReturnToBranchMeters).HasColumnName("return_to_branch_meters");
        builder.Property(e => e.PlannedDrivingDurationSeconds).HasColumnName("planned_driving_duration_seconds");
        builder.Property(e => e.StopCount).HasColumnName("stop_count");
        builder.Property(e => e.ComplexAccessStopCount).HasColumnName("complex_access_stop_count");
        builder.Property(e => e.PerOrderBufferSeconds).HasColumnName("per_order_buffer_seconds").HasDefaultValue(240);
        builder.Property(e => e.ComplexAccessBufferSeconds).HasColumnName("complex_access_buffer_seconds").HasDefaultValue(480);
        builder.Property(e => e.PlanningWarnings).HasColumnName("planning_warnings").HasMaxLength(2000);
        builder.Property(e => e.MetaDurationSeconds).HasColumnName("meta_duration_seconds");
        builder.Property(e => e.ConsolidatedAtUtc).HasColumnName("consolidated_at_utc");
        builder.Property(e => e.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(e => e.ActualDurationSeconds).HasColumnName("actual_duration_seconds");
        builder.Property(e => e.MetSla).HasColumnName("met_sla");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(e => e.Deliveryman)
            .WithMany()
            .HasForeignKey(e => e.DeliverymanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Branch)
            .WithMany()
            .HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.DeliverymanId, e.BranchId, e.Status })
            .HasDatabaseName("idx_delivery_route_dm_branch_status");

        builder.HasMany(e => e.Stops)
            .WithOne(s => s.DeliveryRoute)
            .HasForeignKey(s => s.DeliveryRouteId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLower(input[0]));
        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                result.Append('_');
                result.Append(char.ToLower(input[i]));
            }
            else result.Append(input[i]);
        }
        return result.ToString();
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new System.Text.StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in input)
        {
            if (c == '_')
                capitalizeNext = true;
            else if (capitalizeNext)
            {
                result.Append(char.ToUpper(c));
                capitalizeNext = false;
            }
            else result.Append(c);
        }
        return result.ToString();
    }
}
