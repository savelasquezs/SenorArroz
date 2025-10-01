using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customer");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(c => c.Phone1).HasColumnName("phone1").HasMaxLength(10).IsRequired();
        builder.Property(c => c.Phone2).HasColumnName("phone2").HasMaxLength(10);
        builder.Property(c => c.Active).HasColumnName("active").HasDefaultValue(true);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore); ; ;
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // Relaciones
        builder.HasOne(c => c.Branch)
            .WithMany(b => b.Customers)
            .HasForeignKey(c => c.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices
        builder.HasIndex(c => c.BranchId).HasDatabaseName("idx_customer_branch");
        builder.HasIndex(c => c.Phone1).HasDatabaseName("idx_customer_phone");
        builder.HasIndex(c => c.Active).HasDatabaseName("idx_customer_active").HasFilter("active = true");
    }
}
