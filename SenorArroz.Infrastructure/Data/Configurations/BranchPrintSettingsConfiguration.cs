using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

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
        builder.Property(s => s.EnableKitchenJobs).HasColumnName("enable_kitchen_jobs");
        builder.Property(s => s.EnableDeliveryJobs).HasColumnName("enable_delivery_jobs");
        builder.Property(s => s.EnableCashierJobs).HasColumnName("enable_cashier_jobs");
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
}
