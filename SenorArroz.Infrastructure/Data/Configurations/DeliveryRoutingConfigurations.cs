using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class DeliveryRoutingPlanConfiguration : IEntityTypeConfiguration<DeliveryRoutingPlan>
{
    public void Configure(EntityTypeBuilder<DeliveryRoutingPlan> builder)
    {
        builder.ToTable("delivery_routing_plan");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.GenerationNumber).HasColumnName("generation_number").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(
            value => DeliveryRoutingEnumConversion.ToSnakeCase(value.ToString()),
            value => DeliveryRoutingEnumConversion.Parse<DeliveryRoutingPlanStatus>(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.GeneratedAtUtc).HasColumnName("generated_at_utc").IsRequired();
        builder.Property(x => x.InputFingerprint).HasColumnName("input_fingerprint").HasMaxLength(128).IsRequired();
        builder.Property(x => x.AvailableSlotCount).HasColumnName("available_slot_count").IsRequired();
        builder.Property(x => x.SoonSlotCount).HasColumnName("soon_slot_count").IsRequired();
        builder.Property(x => x.SolverDurationMs).HasColumnName("solver_duration_ms").IsRequired();
        builder.Property(x => x.MatrixSource).HasColumnName("matrix_source").HasConversion(
            value => DeliveryRoutingEnumConversion.ToSnakeCase(value.ToString()),
            value => DeliveryRoutingEnumConversion.Parse<RoutingMatrixSource>(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Warnings).HasColumnName("warnings").HasMaxLength(4000);
        ConfigureAudit(builder);

        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BranchId, x.Status }).HasDatabaseName("idx_delivery_routing_plan_branch_status");
        builder.HasIndex(x => new { x.BranchId, x.GenerationNumber }).IsUnique().HasDatabaseName("uq_delivery_routing_plan_generation");
    }

    internal static void ConfigureAudit<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<DateTime>("CreatedAt").HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property<DateTime>("UpdatedAt").HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}

public sealed class DeliveryRouteProposalConfiguration : IEntityTypeConfiguration<DeliveryRouteProposal>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteProposal> builder)
    {
        builder.ToTable("delivery_route_proposal");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DeliveryRoutingPlanId).HasColumnName("delivery_routing_plan_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(
            value => DeliveryRoutingEnumConversion.ToSnakeCase(value.ToString()),
            value => DeliveryRoutingEnumConversion.Parse<DeliveryRouteProposalStatus>(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Recommendation).HasColumnName("recommendation").HasConversion(
            value => DeliveryRoutingEnumConversion.ToSnakeCase(value.ToString()),
            value => DeliveryRoutingEnumConversion.Parse<DeliveryRouteRecommendation>(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExpectedDepartureAtUtc).HasColumnName("expected_departure_at_utc").IsRequired();
        builder.Property(x => x.WaitSeconds).HasColumnName("wait_seconds").IsRequired();
        builder.Property(x => x.ApproximateDrivingDurationSeconds).HasColumnName("approximate_driving_duration_seconds").IsRequired();
        builder.Property(x => x.ApproximateDistanceMeters).HasColumnName("approximate_distance_meters").IsRequired();
        builder.Property(x => x.ValidatedDrivingDurationSeconds).HasColumnName("validated_driving_duration_seconds");
        builder.Property(x => x.ValidatedDistanceMeters).HasColumnName("validated_distance_meters");
        builder.Property(x => x.GoogleValidationStatus).HasColumnName("google_validation_status").HasConversion(
            value => DeliveryRoutingEnumConversion.ToSnakeCase(value.ToString()),
            value => DeliveryRoutingEnumConversion.Parse<GoogleRouteValidationStatus>(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastDeliverySeconds).HasColumnName("last_delivery_seconds").IsRequired();
        builder.Property(x => x.WorstAgeAtDeliverySeconds).HasColumnName("worst_age_at_delivery_seconds").IsRequired();
        builder.Property(x => x.DirectionSpreadDegrees).HasColumnName("direction_spread_degrees").IsRequired();
        builder.Property(x => x.Score).HasColumnName("score").IsRequired();
        builder.Property(x => x.ClaimedByDeliverymanId).HasColumnName("claimed_by_deliveryman_id");
        builder.Property(x => x.ClaimedAtUtc).HasColumnName("claimed_at_utc");
        builder.Property(x => x.PlanningWarnings).HasColumnName("planning_warnings").HasMaxLength(4000);
        DeliveryRoutingPlanConfiguration.ConfigureAudit(builder);

        builder.HasOne(x => x.DeliveryRoutingPlan).WithMany(x => x.Proposals).HasForeignKey(x => x.DeliveryRoutingPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ClaimedByDeliveryman).WithMany().HasForeignKey(x => x.ClaimedByDeliverymanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeliveryRoutingPlanId, x.Sequence }).IsUnique().HasDatabaseName("uq_delivery_route_proposal_sequence");
        builder.HasIndex(x => new { x.DeliveryRoutingPlanId, x.Status }).HasDatabaseName("idx_delivery_route_proposal_plan_status");
    }
}

internal static class DeliveryRoutingEnumConversion
{
    public static string ToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]))
                result.Append('_');
            result.Append(char.ToLowerInvariant(value[index]));
        }
        return result.ToString();
    }

    public static T Parse<T>(string value) where T : struct, Enum =>
        Enum.Parse<T>(string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])), true);
}

public sealed class DeliveryRouteProposalStopConfiguration : IEntityTypeConfiguration<DeliveryRouteProposalStop>
{
    public void Configure(EntityTypeBuilder<DeliveryRouteProposalStop> builder)
    {
        builder.ToTable("delivery_route_proposal_stop");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DeliveryRoutingPlanId).HasColumnName("delivery_routing_plan_id").IsRequired();
        builder.Property(x => x.DeliveryRouteProposalId).HasColumnName("delivery_route_proposal_id");
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.StopSequence).HasColumnName("stop_sequence");
        builder.Property(x => x.EstimatedReadyAtUtc).HasColumnName("estimated_ready_at_utc").IsRequired();
        builder.Property(x => x.EstimatedArrivalAtUtc).HasColumnName("estimated_arrival_at_utc");
        builder.Property(x => x.TravelFromPreviousSeconds).HasColumnName("travel_from_previous_seconds").IsRequired();
        builder.Property(x => x.ServiceSeconds).HasColumnName("service_seconds").IsRequired();
        builder.Property(x => x.BearingFromBranchDegrees).HasColumnName("bearing_from_branch_degrees").IsRequired();
        builder.Property(x => x.WasReadyAtGeneration).HasColumnName("was_ready_at_generation").IsRequired();
        builder.Property(x => x.IsSuggestedWait).HasColumnName("is_suggested_wait").IsRequired();
        builder.Property(x => x.UnroutedReason).HasColumnName("unrouted_reason").HasMaxLength(256);
        DeliveryRoutingPlanConfiguration.ConfigureAudit(builder);

        builder.HasOne(x => x.DeliveryRoutingPlan).WithMany(x => x.Stops).HasForeignKey(x => x.DeliveryRoutingPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DeliveryRouteProposal).WithMany(x => x.Stops).HasForeignKey(x => x.DeliveryRouteProposalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeliveryRoutingPlanId, x.OrderId }).IsUnique().HasDatabaseName("uq_delivery_route_proposal_stop_plan_order");
        builder.HasIndex(x => new { x.DeliveryRouteProposalId, x.StopSequence }).HasDatabaseName("idx_delivery_route_proposal_stop_sequence");
    }
}
