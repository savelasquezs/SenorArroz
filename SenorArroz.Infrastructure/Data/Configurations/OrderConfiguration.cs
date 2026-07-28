using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("order"); // Comillas porque order es palabra reservada

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(o => o.TakenById).HasColumnName("taken_by_id").IsRequired();
        builder.Property(o => o.CustomerId).HasColumnName("customer_id");
        builder.Property(o => o.AddressId).HasColumnName("address_id");
        builder.Property(o => o.LoyaltyCycleStepId).HasColumnName("loyalty_cycle_step_id");
        builder.Property(o => o.LoyaltyRewardSnapshot).HasColumnName("loyalty_reward_snapshot").HasMaxLength(500);
        builder.Property(o => o.DeliveryRouteId).HasColumnName("delivery_route_id");
        builder.Property(o => o.DeliveryManId).HasColumnName("delivery_man_id");
        builder.Property(o => o.GuestName).HasColumnName("guestname").HasMaxLength(100);
        builder.Property(o => o.DeliveryAppConnectionId).HasColumnName("delivery_app_connection_id");
        builder.Property(o => o.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(160);
        builder.Property(o => o.OrderSource).HasColumnName("order_source").HasMaxLength(40);
        builder.Property(o => o.ExternalFulfillmentProvider).HasColumnName("external_fulfillment_provider").HasMaxLength(40);
        builder.Property(o => o.ExternalStoreName).HasColumnName("external_store_name").HasMaxLength(200);
        builder.Property(o => o.ExternalCustomerPhone).HasColumnName("external_customer_phone").HasMaxLength(50);
        builder.Property(o => o.ExternalDeliveryAddress).HasColumnName("external_delivery_address").HasMaxLength(600);
        builder.Property(o => o.ExternalTotalDiscounts).HasColumnName("external_total_discounts");
        builder.Property(o => o.ExternalDiscountByRappi).HasColumnName("external_discount_by_rappi");
        builder.Property(o => o.ExternalDiscountByPartner).HasColumnName("external_discount_by_partner");
        builder.Property(o => o.ExternalCharges).HasColumnName("external_charges");

        // Enum conversions
        builder.Property(o => o.Type).HasColumnName("type").HasConversion(
             v => v.HasValue ? ToSnakeCase(v.Value.ToString()) : null,
                v => string.IsNullOrEmpty(v) ? null : Enum.Parse<OrderType>(ToPascalCase(v), true)
            ).IsRequired().HasDefaultValue(OrderType.Delivery);
        builder.Property(o => o.Status).HasColumnName("status").HasConversion(
             v => ToSnakeCase(v.ToString()),
                v => Enum.Parse<OrderStatus>(ToPascalCase(v), true)
            ).IsRequired();

        builder.Property(o => o.DeliveryFee).HasColumnName("delivery_fee");
        builder.Property(o => o.ReservedFor).HasColumnName("reserved_for");
        builder.Property(o => o.PrepareAt).HasColumnName("prepare_at");
        builder.Property(o => o.PreparedNotifiedAt).HasColumnName("prepared_notified_at");
        builder.Property(o => o.StatusTimes).HasColumnName("status_times").HasColumnType("jsonb");

        // Totales: la aplicación los calcula (p. ej. OrderTotalsHelper) y deben persistirse/leerse en el grafo EF.
        // ValueGeneratedOnAddOrUpdate + Ignore hacía que, tras crear el pedido, Order.Total quedara 0 en memoria al
        // consultar de nuevo — cap de efectivo en tienda = 0 y Apply fallaba con "no hay remanente".
        builder.Property(o => o.Subtotal)
            .HasColumnName("subtotal")
            .HasDefaultValue(0);

        builder.Property(o => o.Total)
            .HasColumnName("total")
            .HasDefaultValue(0);

        builder.Property(o => o.DiscountTotal)
            .HasColumnName("discount_total")
            .HasDefaultValue(0);
        builder.Property(o => o.FreeDeliveryRequested)
            .HasColumnName("free_delivery_requested")
            .HasDefaultValue(false);
        builder.Property(o => o.AppliedBenefitType)
            .HasColumnName("applied_benefit_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .HasDefaultValue(OrderBenefitType.None);
        builder.Property(o => o.AppliedBenefitSourceId).HasColumnName("applied_benefit_source_id");
        builder.Property(o => o.AppliedBenefitCode).HasColumnName("applied_benefit_code").HasMaxLength(80);
        builder.Property(o => o.AppliedBenefitLabel).HasColumnName("applied_benefit_label").HasMaxLength(250);
        builder.Property(o => o.AppliedBenefitRewardType)
            .HasColumnName("applied_benefit_reward_type")
            .HasConversion<string>()
            .HasMaxLength(40);
        builder.Property(o => o.AppliedBenefitAmount).HasColumnName("applied_benefit_amount").HasPrecision(10, 2);
        builder.Property(o => o.AppliedBenefitSnapshot).HasColumnName("applied_benefit_snapshot").HasColumnType("jsonb");
        builder.Property(o => o.ManualBenefitReason).HasColumnName("manual_benefit_reason").HasMaxLength(500);
        builder.Property(o => o.ManualBenefitGrantedByUserId).HasColumnName("manual_benefit_granted_by_user_id");
        builder.Property(o => o.ManualBenefitGrantedByUserName).HasColumnName("manual_benefit_granted_by_user_name").HasMaxLength(150);
        builder.Property(o => o.ManualBenefitGrantedAt).HasColumnName("manual_benefit_granted_at");
        builder.Property(o => o.ManualBenefitGiftProductId).HasColumnName("manual_benefit_gift_product_id");
        builder.Property(o => o.Notes).HasColumnName("notes").HasMaxLength(200);
        builder.Property(o => o.CancelledReason).HasColumnName("cancelled_reason").HasMaxLength(200);

        builder.Property(o => o.PaidInStoreCash).HasColumnName("paid_in_store_cash").HasDefaultValue(false);
        builder.Property(o => o.PaidInStoreCashAt).HasColumnName("paid_in_store_cash_at");
        builder.Property(o => o.PaidInStoreCashAmount).HasColumnName("paid_in_store_cash_amount");

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // Relaciones
        builder.HasOne(o => o.Branch)
            .WithMany(b => b.Orders)
            .HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.TakenBy)
            .WithMany(u => u.TakenOrders)
            .HasForeignKey(o => o.TakenById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.Address)
            .WithMany(a => a.Orders)
            .HasForeignKey(o => o.AddressId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.LoyaltyCycleStep)
            .WithMany(s => s.Orders)
            .HasForeignKey(o => o.LoyaltyCycleStepId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.DeliveryMan)
            .WithMany(u => u.DeliveryOrders)
            .HasForeignKey(o => o.DeliveryManId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.DeliveryRoute)
            .WithMany()
            .HasForeignKey(o => o.DeliveryRouteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.DeliveryAppConnection)
            .WithMany()
            .HasForeignKey(o => o.DeliveryAppConnectionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Índices
        builder.HasIndex(o => o.BranchId).HasDatabaseName("idx_order_branch");
        builder.HasIndex(o => o.CustomerId).HasDatabaseName("idx_order_customer");
        builder.HasIndex(o => o.Status).HasDatabaseName("idx_order_status");
        builder.HasIndex(o => o.Type).HasDatabaseName("idx_order_type");
        builder.HasIndex(o => o.CreatedAt).HasDatabaseName("idx_order_date");
        builder.HasIndex(o => o.DeliveryManId).HasDatabaseName("idx_order_delivery_man");
        builder.HasIndex(o => o.DeliveryRouteId).HasDatabaseName("idx_order_delivery_route");
        builder.HasIndex(o => o.LoyaltyCycleStepId).HasDatabaseName("idx_order_loyalty_cycle_step");
        builder.HasIndex(o => new { o.DeliveryAppConnectionId, o.ExternalOrderId }).IsUnique().HasDatabaseName("ux_order_external_source");
    }

    /// <summary>
    /// Convierte PascalCase/camelCase a snake_case
    /// Ejemplo: InPreparation -> in_preparation, OnTheWay -> on_the_way
    /// </summary>
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
            else
            {
                result.Append(input[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Convierte snake_case a PascalCase
    /// Ejemplo: in_preparation -> InPreparation, on_the_way -> OnTheWay
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new System.Text.StringBuilder();
        bool capitalizeNext = true;

        foreach (char c in input)
        {
            if (c == '_')
            {
                capitalizeNext = true;
            }
            else if (capitalizeNext)
            {
                result.Append(char.ToUpper(c));
                capitalizeNext = false;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
