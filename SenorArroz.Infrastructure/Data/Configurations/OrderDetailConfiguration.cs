using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> builder)
    {
        builder.ToTable("order_detail");

        builder.HasKey(od => od.Id);
        builder.Property(od => od.Id).HasColumnName("id");

        builder.Property(od => od.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(od => od.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(od => od.Quantity).HasColumnName("quantity").HasDefaultValue(1);
        builder.Property(od => od.UnitPrice).HasColumnName("unit_price").HasDefaultValue(0);
        builder.Property(od => od.Discount).HasColumnName("discount").HasDefaultValue(0);
        builder.Property(od => od.Subtotal).HasColumnName("subtotal");
        builder.Property(od => od.Notes).HasColumnName("notes");

        builder.Property(od => od.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(od => od.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Relaciones
        builder.HasOne(od => od.Order)
            .WithMany(o => o.OrderDetails)
            .HasForeignKey(od => od.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(od => od.Product)
            .WithMany(p => p.OrderDetails)
            .HasForeignKey(od => od.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices
        builder.HasIndex(od => od.OrderId).HasDatabaseName("idx_order_detail_order");
        builder.HasIndex(od => od.ProductId).HasDatabaseName("idx_order_detail_product");
    }
}