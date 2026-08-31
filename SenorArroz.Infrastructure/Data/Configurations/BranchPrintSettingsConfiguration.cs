using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BranchPrintSettingsConfiguration : IEntityTypeConfiguration<BranchPrintSettings>
{
    public void Configure(EntityTypeBuilder<BranchPrintSettings> builder)
    {
        builder.ToTable("branch_print_settings");

        builder.HasKey(s => s.BranchId);
        builder.Property(s => s.BranchId).HasColumnName("branch_id");

        builder.Property(s => s.KitchenHeaderLine1).HasColumnName("kitchen_header_line1").HasMaxLength(80);
        builder.Property(s => s.KitchenHeaderLine2).HasColumnName("kitchen_header_line2").HasMaxLength(80);
        builder.Property(s => s.ShowKitchenOrderNumber).HasColumnName("show_kitchen_order_number");
        builder.Property(s => s.ShowKitchenTime).HasColumnName("show_kitchen_time");
        builder.Property(s => s.ShowKitchenNotes).HasColumnName("show_kitchen_notes");
        builder.Property(s => s.DeliveryShowLineSubtotals).HasColumnName("delivery_show_line_subtotals");
        builder.Property(s => s.DeliveryShowPayments).HasColumnName("delivery_show_payments");
        builder.Property(s => s.DeliveryShowLoyaltyFooter).HasColumnName("delivery_show_loyalty_footer");
        builder.Property(s => s.CashierMirrorDeliveryLayout).HasColumnName("cashier_mirror_delivery_layout");
        builder.Property(s => s.FooterMessageKitchen).HasColumnName("footer_message_kitchen").HasMaxLength(200);
        builder.Property(s => s.FooterMessageDelivery).HasColumnName("footer_message_delivery").HasMaxLength(200);
        builder.Property(s => s.FooterMessageCashier).HasColumnName("footer_message_cashier").HasMaxLength(200);
        builder.Property(s => s.PaperWidthMm).HasColumnName("paper_width_mm");
        builder.Property(s => s.PaperWidthMmKitchen).HasColumnName("paper_width_mm_kitchen");
        builder.Property(s => s.PaperWidthMmDelivery).HasColumnName("paper_width_mm_delivery");
        builder.Property(s => s.PaperWidthMmCashier).HasColumnName("paper_width_mm_cashier");
        builder.Property(s => s.EnableKitchenJobs).HasColumnName("enable_kitchen_jobs");
        builder.Property(s => s.EnableDeliveryJobs).HasColumnName("enable_delivery_jobs");
        builder.Property(s => s.EnableCashierJobs).HasColumnName("enable_cashier_jobs");
        builder.Property(s => s.KitchenAutoPrintTrigger)
            .HasColumnName("kitchen_auto_print_trigger")
            .HasMaxLength(30)
            .HasConversion(v => ToDb(v), v => FromDb(v));
        builder.Property(s => s.PrinterQueueKitchen).HasColumnName("printer_queue_kitchen").HasMaxLength(128);
        builder.Property(s => s.PrinterQueueDelivery).HasColumnName("printer_queue_delivery").HasMaxLength(128);
        builder.Property(s => s.PrinterQueueCashier).HasColumnName("printer_queue_cashier").HasMaxLength(128);
        builder.Property(s => s.ReceiptLogoPath).HasColumnName("receipt_logo_path").HasMaxLength(500);
        builder.Property(s => s.AgentTokenHash).HasColumnName("agent_token_hash").HasMaxLength(128).IsRequired();
        builder.Property(s => s.AgentTokenSalt).HasColumnName("agent_token_salt").HasMaxLength(64).IsRequired();
        builder.Property(s => s.AgentTokenUpdatedAt).HasColumnName("agent_token_updated_at");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(s => s.Branch)
            .WithOne(b => b.PrintSettings)
            .HasForeignKey<BranchPrintSettings>(s => s.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static string ToDb(KitchenAutoPrintTrigger value) => value switch
    {
        KitchenAutoPrintTrigger.WhenMarkedReady => "when_marked_ready",
        KitchenAutoPrintTrigger.WhenOrderCreated => "when_order_created",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static KitchenAutoPrintTrigger FromDb(string value) => value switch
    {
        "when_marked_ready" => KitchenAutoPrintTrigger.WhenMarkedReady,
        "when_order_created" => KitchenAutoPrintTrigger.WhenOrderCreated,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
