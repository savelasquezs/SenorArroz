using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(p => p.Price).HasColumnName("price").IsRequired();
        builder.Property(p => p.Stock).HasColumnName("stock");
        builder.Property(p => p.WeightGrams).HasColumnName("weight_grams");
        builder.Property(p => p.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(p => p.CommercialProfileId).HasColumnName("commercial_profile_id");
        builder.Property(p => p.ServesPeopleMin).HasColumnName("serves_people_min");
        builder.Property(p => p.ServesPeopleMax).HasColumnName("serves_people_max");
        builder.Property(p => p.StorefrontVariantLabel).HasColumnName("storefront_variant_label").HasMaxLength(80);
        builder.Property(p => p.StorefrontSortOrder).HasColumnName("storefront_sort_order").HasDefaultValue(0);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // Relaciones
        builder.HasOne(p => p.Category)
            .WithMany(pc => pc.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.CommercialProfile).WithMany(x => x.Products)
            .HasForeignKey(p => p.CommercialProfileId).OnDelete(DeleteBehavior.SetNull);

        // Índices
        builder.HasIndex(p => p.CategoryId).HasDatabaseName("idx_product_category");
        builder.HasIndex(p => p.Active).HasDatabaseName("idx_product_active").HasFilter("active = true");
    }
}
