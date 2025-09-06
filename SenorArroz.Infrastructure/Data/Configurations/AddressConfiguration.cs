using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("address");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(a => a.NeighborhoodId).HasColumnName("neighborhood_id").IsRequired();
        builder.Property(a => a.AddressText).HasColumnName("address").HasMaxLength(200).IsRequired();
        builder.Property(a => a.AdditionalInfo).HasColumnName("additional_info").HasMaxLength(150);
        builder.Property(a => a.DeliveryFee).HasColumnName("delivery_fee").IsRequired();
        builder.Property(a => a.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,6)");
        builder.Property(a => a.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,6)");

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Relaciones
        builder.HasOne(a => a.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Neighborhood)
            .WithMany(n => n.Addresses)
            .HasForeignKey(a => a.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices
        builder.HasIndex(a => a.CustomerId).HasDatabaseName("idx_address_customer");
        builder.HasIndex(a => a.NeighborhoodId).HasDatabaseName("idx_address_neighborhood");
    }
}