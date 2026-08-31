using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("print_job");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id");

        builder.Property(j => j.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(j => j.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired()
            .HasConversion(
                v => KindToDb(v),
                v => KindFromDb(v));
        builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(20).IsRequired()
            .HasConversion(
                v => StatusToDb(v),
                v => StatusFromDb(v));
        builder.Property(j => j.OrderIdsJson).HasColumnName("order_ids_json").IsRequired()
            .HasColumnType("jsonb");
        builder.Property(j => j.PayloadJson).HasColumnName("payload_json").IsRequired()
            .HasColumnType("jsonb");
        builder.Property(j => j.PayloadVersion).HasColumnName("payload_version");
        builder.Property(j => j.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(j => j.StartedAt).HasColumnName("started_at");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at");
        builder.Property(j => j.AutomaticOrderId).HasColumnName("automatic_order_id");
        builder.Property(j => j.AutomaticTrigger).HasColumnName("automatic_trigger").HasMaxLength(30)
            .HasConversion(
                v => v.HasValue ? AutoTriggerToDb(v.Value) : null,
                v => v == null ? null : AutoTriggerFromDb(v));

        builder.HasOne(j => j.Branch)
            .WithMany()
            .HasForeignKey(j => j.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(j => new { j.BranchId, j.Kind, j.CreatedAt, j.Id })
            .HasDatabaseName("ix_print_job_pending_branch_kind_created")
            .HasFilter("status = 'pending'");

        builder.HasIndex(j => new { j.BranchId, j.Kind, j.AutomaticOrderId, j.AutomaticTrigger })
            .IsUnique()
            .HasDatabaseName("ux_print_job_automatic_event")
            .HasFilter("automatic_order_id IS NOT NULL AND automatic_trigger IS NOT NULL");
    }

    private static string KindToDb(PrintJobKind k) => k switch
    {
        PrintJobKind.Kitchen => Roles.Kitchen,
        PrintJobKind.Delivery => "delivery",
        PrintJobKind.Cashier => Roles.Cashier,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    private static PrintJobKind KindFromDb(string v) => v?.ToLowerInvariant() switch
    {
        Roles.Kitchen => PrintJobKind.Kitchen,
        "delivery" => PrintJobKind.Delivery,
        Roles.Cashier => PrintJobKind.Cashier,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static string StatusToDb(PrintJobStatus s) => s switch
    {
        PrintJobStatus.Pending => "pending",
        PrintJobStatus.Processing => "processing",
        PrintJobStatus.Done => "done",
        PrintJobStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    private static PrintJobStatus StatusFromDb(string v) => v?.ToLowerInvariant() switch
    {
        "pending" => PrintJobStatus.Pending,
        "processing" => PrintJobStatus.Processing,
        "done" => PrintJobStatus.Done,
        "failed" => PrintJobStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static string AutoTriggerToDb(KitchenAutoPrintTrigger value) => value switch
    {
        KitchenAutoPrintTrigger.WhenMarkedReady => "when_marked_ready",
        KitchenAutoPrintTrigger.WhenOrderCreated => "when_order_created",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static KitchenAutoPrintTrigger AutoTriggerFromDb(string value) => value switch
    {
        "when_marked_ready" => KitchenAutoPrintTrigger.WhenMarkedReady,
        "when_order_created" => KitchenAutoPrintTrigger.WhenOrderCreated,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
