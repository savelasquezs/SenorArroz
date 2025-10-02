using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_category");

        builder.HasKey(pc => pc.Id);
        builder.Property(pc => pc.Id).HasColumnName("id");

        builder.Property(pc => pc.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(pc => pc.Name).HasColumnName("name").HasMaxLength(150).IsRequired();

        builder.Property(pc => pc.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(pc => pc.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(pc => pc.Branch)
            .WithMany(b => b.ProductCategories)
            .HasForeignKey(pc => pc.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}