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

        builder.Property(o => o.Subtotal)
            .HasColumnName("subtotal")
            .HasDefaultValue(0)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(o => o.Total)
            .HasColumnName("total")
            .HasDefaultValue(0)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(o => o.DiscountTotal)
            .HasColumnName("discount_total")
            .HasDefaultValue(0)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
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

        // Índices
        builder.HasIndex(o => o.BranchId).HasDatabaseName("idx_order_branch");
        builder.HasIndex(o => o.CustomerId).HasDatabaseName("idx_order_customer");
        builder.HasIndex(o => o.Status).HasDatabaseName("idx_order_status");
        builder.HasIndex(o => o.Type).HasDatabaseName("idx_order_type");
        builder.HasIndex(o => o.CreatedAt).HasDatabaseName("idx_order_date");
        builder.HasIndex(o => o.DeliveryManId).HasDatabaseName("idx_order_delivery_man");
        builder.HasIndex(o => o.DeliveryRouteId).HasDatabaseName("idx_order_delivery_route");
        builder.HasIndex(o => o.LoyaltyCycleStepId).HasDatabaseName("idx_order_loyalty_cycle_step");
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